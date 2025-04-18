using Microsoft.IO;
using Snowflake.Client.Helpers;
using Snowflake.Client.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Snowflake.Client
{
    public static class SnowflakeDataMapper
    {
        private const int DefaultBufferSize = 1024;
        private static readonly RecyclableMemoryStreamManager _recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();
        private static readonly Encoding __utf8EncodingNoBom = new UTF8Encoding(false);
        private static JsonSerializerOptions __jsonMapperOptions = new JsonSerializerOptions();

        [AttributeUsage(AttributeTargets.Property)]
        public class Snowflake_ColumnNameAttribute : Attribute
        {
            public string ColumnName { get; }

            public Snowflake_ColumnNameAttribute(string columnName)
            {
                ColumnName = columnName;
            }
        }
        public static void Configure(JsonSerializerOptions jsonMapperOptions)
        {
            if (jsonMapperOptions != null)
                __jsonMapperOptions = jsonMapperOptions;
        }

        [Obsolete("Please use Configure method instead")]
        public static void SetJsonMapperOptions(JsonSerializerOptions jsonMapperOptions)
        {
            Configure(jsonMapperOptions);
        }

        public static T MapTo<T>(ColumnDescription column, string value)
        {
            if (column == null)
                throw new ArgumentNullException(nameof(column));

            // Get a Recyclable memory stream to write the json content into.
            using (MemoryStream ms = _recyclableMemoryStreamManager.GetStream())
            {
                // Create a stream writer that will encode characters to utf8 with no byte-order-mark.
                using(StreamWriter sw = new StreamWriter(ms, __utf8EncodingNoBom, DefaultBufferSize, true))
                {
                    // Write the JSON into the stream writer.
                    ConvertColumnValueToJsonToken(value, column.Type, sw);

                    // Dispose of the stream writer to flush all character into the memory stream.
                }

                // Reset the stream's position to the start, ready for reading the json content that was just
                // written into it.
                ms.Position = 0;

                // Deserialize the JSON to the required object type.
                T val = JsonSerializer.Deserialize<T>(ms, __jsonMapperOptions);
                return val;
            }
        }

        private static readonly Dictionary<Type, Dictionary<string, Action<object, object>>> setterCache = new();
        public static IEnumerable<T> MapToObject<T>(List<ColumnDescription> columns, List<List<string>> rows)
        {
            var resultList = new List<T>();
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Cache property mappings.
            var propertyMappings = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(prop => new
                {
                    Property = prop,
                    ColumnName = prop.GetCustomAttributes(typeof(Snowflake_ColumnNameAttribute), false)
                                     .OfType<Snowflake_ColumnNameAttribute>()
                                     .FirstOrDefault()?.ColumnName?.ToUpperInvariant() ?? prop.Name.ToUpperInvariant()
                })
                .ToList();
            var columnLookup = columns.Select((column, index) => new { Column = column, Index = index }).ToDictionary(c => c.Column.Name.ToUpperInvariant(), c => c.Index);
            foreach (var row in rows)
            {
                var item = Activator.CreateInstance<T>();
                foreach (var mapping in propertyMappings)
                {
                    if (columnLookup.TryGetValue(mapping.ColumnName, out int columnIndex) && row[columnIndex] != null)
                    {
                        var valueStr = row[columnIndex];
                        var prop = mapping.Property;
                        object value = null;

                        // Directly handle known types for faster parsing.
                        if (prop.PropertyType == typeof(int)) value = int.Parse(valueStr);
                        else if (prop.PropertyType == typeof(double)) value = double.Parse(valueStr);
                        else if (prop.PropertyType == typeof(DateTime)) value = epoch.AddSeconds(double.Parse(valueStr));
                        else if (prop.PropertyType == typeof(bool))
                            value = valueStr == "1";// || valueStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                        else value = Convert.ChangeType(valueStr, prop.PropertyType);

                        prop.SetValue(item, value);
                    }
                }
                resultList.Add(item);
            }
            return resultList;
        }

        public static IEnumerable<T> MapTo<T>(List<ColumnDescription> columns, List<List<string>> rows)
        {
            if (columns == null || columns.Count == 0)
                throw new ArgumentNullException(nameof(columns));

            if (rows == null)
                throw new ArgumentNullException(nameof(rows));

            // Get a RecyclableMemoryStream to write the json content into.
            using (MemoryStream ms = _recyclableMemoryStreamManager.GetStream())
            {
                // Create a stream writer that will encode characters to utf8 with no byte-order-mark.
                using(StreamWriter sw = new StreamWriter(ms, __utf8EncodingNoBom, DefaultBufferSize, true))
                { 
                    foreach(var rowRecord in rows)
                    {
                        // Write the JSON into the stream writer.
                        BuildJsonString(columns, rowRecord, sw);

                        // Flush any buffered content into the memory stream.
                        sw.Flush();

                        // Reset the stream's position to the start, ready for reading the json content that was just
                        // written into it.
                        ms.Position = 0;

                        // Deserialize the JSON to the required object type.
                        T val = JsonSerializer.Deserialize<T>(ms, __jsonMapperOptions);
                        yield return val;

                        // Reset the memory stream for re-use in the next loop.
                        ms.SetLength(0);
                    }
                }
            }
        }

        private static void BuildJsonString(List<ColumnDescription> columns, List<string> rowRecord, TextWriter tw)
        {
            // Append json opening brace.
            tw.Write('{');

            if (columns.Count != 0)
            {
                // Append first property.
                AppendAsJsonProperty(columns[0].Name, rowRecord[0], columns[0].Type, tw);

                // Append all other properties, prefixed with a comma to separate from previous property.
                for (int i = 1; i < columns.Count; i++)
                {
                    tw.Write(",");
                    AppendAsJsonProperty(columns[i].Name, rowRecord[i], columns[i].Type, tw);
                }
            }

            // Append json closing brace.
            tw.Write('}');
        }

        private static void AppendAsJsonProperty(
            string propertyName,
            string columnValue,
            string columnType,
            TextWriter tw)
        {
            // Append property name and colon separator.
            tw.Write('"');
            tw.Write(propertyName);
            tw.Write("\":");

            // Append json property value.
            ConvertColumnValueToJsonToken(columnValue, columnType, tw);
        }

        private static void ConvertColumnValueToJsonToken(
            string value,
            string columnType,
            TextWriter tw)
        {
            if(value is null || value == "null")
            {
                tw.Write("null");
                return;
            }

            switch(columnType)
            {
                case "text":
                    tw.Write(JsonSerializer.Serialize(value));
                    break;

                case "fixed":
                case "real":
                    tw.Write(value);
                    break;

                case "boolean":
                    tw.Write(value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false");
                    break;

                case "date":
                case "time":
                case "timestamp_ntz":
                    tw.Write('"');
                    tw.Write(SnowflakeTypesConverter.ConvertToDateTime(value, columnType).ToString("o"));
                    tw.Write('"');
                    break;

                case "timestamp_ltz":
                case "timestamp_tz":
                    tw.Write('"');
                    tw.Write(SnowflakeTypesConverter.ConvertToDateTimeOffset(value, columnType).ToString("o"));
                    tw.Write('"');
                    break;

                case "object":
                case "variant":
                case "array":
                    tw.Write(value);
                    break;

                case "binary":
                    tw.Write('"');
                    HexUtils.HexToBase64(value, tw);
                    tw.Write('"');
                    break;

                default:
                    tw.Write(value);
                    break;
            }
        }
    }
}