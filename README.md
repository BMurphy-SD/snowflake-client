# snowflake-client
Snowflake Client with Stored Procedure support

This is an extension of this library https://github.com/fixer-m/snowflake-db-net-client/tree/master

This is a .NET 8 version

Example Code:

```
using Snowflake.Client;

namespace Syrinx.Common.Models
{
    public class SnowflakeBaseClass
    {
        public static readonly SnowflakeClient snowflakeClient;

        static SnowflakeBaseClass()
        {
            string[] connection = (Configuration.GetConnectionString("SnowflakeConnection") ?? "|||").Split('|');
            snowflakeClient = new SnowflakeClient(connection[0], connection[1], connection[2], connection[3]);
            snowflakeClient.ExecuteAsync("USE WAREHOUSE SYRINX; ").Wait();
        }
    }
}
```

Usage
```
using System.Data;
using System.Text.Json;
using static Syrinx.Common.Models.QueryModel;

namespace SyrinxAPI.Models
{
    public class Query : SnowflakeBaseClass
    {
        public async static Task<ObjectToBeMappedModel> GetSingleRowFromSF(int PARAMETER1, int PARAMETER2, int PARAMETER3)
        {
            return await snowflakeClient.SingleProcedure<ObjectToBeMappedModel>("SYRINX.SCHEMA.STORED_PROCEDURE",
                                        new
                                        {
                                            SP_INPUTVARIABLENAME1 = PARAMETER1,
                                            SP_INPUTVARIABLENAME2 = PARAMETER2,
                                            SP_INPUTVARIABLENAME3 = PARAMETER3
                                        });
        }
        public async static Task<IEnumerable<ObjectToBeMappedModel>> GetMultipleRowsFromSF(int PARAMETER1, int PARAMETER2)
        {
            return await snowflakeClient.QueryProcedure<ObjectToBeMappedModel>("SYRINX.SCHEMA.STORED_PROCEDURE",
                                        new
                                        {
                                            SP_INPUTVARIABLENAME1 = PARAMETER1,
                                            SP_INPUTVARIABLENAME2 = PARAMETER2
                                        });
        }
        public async static Task SendCommandToSF(int PARAMETER1, int PARAMETER2)
        {
            _ = await snowflakeClient.ExecuteProcedure("SYRINX.SCHEMA.STORED_PROCEDURE",
                                new
                                {
                                            SP_INPUTVARIABLENAME1 = PARAMETER1,
                                            SP_INPUTVARIABLENAME2 = PARAMETER2
                                });
        }
        public async static Task<int> GetSingleValueFromSF(int PARAMETER1, int PARAMETER2)
        {
            return await snowflakeClient.ExecuteProcedureScalar<int>("SYRINX.SCHEMA.STORED_PROCEDURE",
                                new
                                {
                                            SP_INPUTVARIABLENAME1 = PARAMETER1,
                                            SP_INPUTVARIABLENAME2 = PARAMETER2
                                });
        }
    }
}
