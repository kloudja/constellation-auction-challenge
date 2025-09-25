using System.Data;
using FluentAssertions;
using Xunit;
using Microsoft.Data.SqlClient;

namespace IntegrationTests;


public class DbIntegrationTests
{
    private static string? ConnStr => Environment.GetEnvironmentVariable("TEST_SQL_CONNSTR");

    [SkippableFact(DisplayName = "Optimistic CAS via rowversion")]
    public async Task RowVersion_CAS_Works()
    {
        Skip.If(string.IsNullOrWhiteSpace(ConnStr), "TEST_SQL_CONNSTR not set; skipping DB test.");

        using var cn = new SqlConnection(ConnStr);
        await cn.OpenAsync();

        var ddl = @"
                IF OBJECT_ID('tempdb..#Auc') IS NOT NULL DROP TABLE #Auc;
                CREATE TABLE #Auc(
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    CurrentHighBid DECIMAL(18,2) NULL,
                    RowVersion ROWVERSION NOT NULL
                );
                INSERT INTO #Auc(Id, CurrentHighBid) VALUES (@id, 100);
                SELECT RowVersion FROM #Auc WHERE Id=@id;";
        var id = Guid.NewGuid();

        using (var cmd = new SqlCommand(ddl, cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            var rv = (byte[])await cmd.ExecuteScalarAsync();

            var cas1 = "UPDATE #Auc SET CurrentHighBid=120 WHERE Id=@id AND RowVersion=@rv; SELECT @@ROWCOUNT;";
            using var c1 = new SqlCommand(cas1, cn);
            c1.Parameters.AddWithValue("@id", id);
            c1.Parameters.Add("@rv", SqlDbType.Timestamp, 8).Value = rv;
            var rows1 = (int)(decimal)await c1.ExecuteScalarAsync();
            rows1.Should().Be(1);

            var sel = "SELECT RowVersion FROM #Auc WHERE Id=@id;";
            using var csel = new SqlCommand(sel, cn); csel.Parameters.AddWithValue("@id", id);
            var rv2 = (byte[])await csel.ExecuteScalarAsync();

            using var c2 = new SqlCommand(cas1, cn);
            c2.Parameters.AddWithValue("@id", id);
            c2.Parameters.Add("@rv", SqlDbType.Timestamp, 8).Value = rv;
            var rows2 = (int)(decimal)await c2.ExecuteScalarAsync();
            rows2.Should().Be(0);
        }
    }

    [SkippableFact(DisplayName = "UNIQUE (AuctionId, SourceRegionId, Sequence) enforces idempotency")]
    public async Task Unique_Composite_Avoids_Duplicates()
    {
        Skip.If(string.IsNullOrWhiteSpace(ConnStr), "TEST_SQL_CONNSTR not set; skipping DB test.");

        using var cn = new SqlConnection(ConnStr);
        await cn.OpenAsync();

        var ddl = @"
            IF OBJECT_ID('tempdb..#Bid') IS NOT NULL DROP TABLE #Bid;
            CREATE TABLE #Bid(
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                AuctionId UNIQUEIDENTIFIER NOT NULL,
                SourceRegionId NVARCHAR(8) NOT NULL,
                Sequence BIGINT NOT NULL,
                Amount DECIMAL(18,2) NOT NULL,
                CONSTRAINT UQ_Bid UNIQUE (AuctionId, SourceRegionId, Sequence)
            );";

        using (var cmd = new SqlCommand(ddl, cn)) await cmd.ExecuteNonQueryAsync();

        var auctionId = Guid.NewGuid();
        var ins = "INSERT INTO #Bid(Id, AuctionId, SourceRegionId, Sequence, Amount) VALUES(@id,@a,'EU',12,300);";
        using (var c1 = new SqlCommand(ins, cn)) { c1.Parameters.AddWithValue("@id", Guid.NewGuid()); c1.Parameters.AddWithValue("@a", auctionId); await c1.ExecuteNonQueryAsync(); }

        using var c2 = new SqlCommand(ins, cn);
        c2.Parameters.AddWithValue("@id", Guid.NewGuid());
        c2.Parameters.AddWithValue("@a", auctionId);
        Func<Task> act = async () => await c2.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<SqlException>();
    }
}
