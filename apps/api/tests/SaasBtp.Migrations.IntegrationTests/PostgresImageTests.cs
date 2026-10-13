using System.Reflection;
using System.Text.RegularExpressions;
using Npgsql;
using SaasBtp.Testing;
using Testcontainers.PostgreSql;

namespace SaasBtp.Migrations.IntegrationTests;

/// <summary>
/// The PostgreSQL of the tests and of local development is initialized like the Supabase
/// project: same locale provider, same locale.
/// </summary>
public sealed partial class PostgresImageTests
{
    [Fact]
    public async Task TheTestPostgresReportsTheIcuProviderAndTheEnUsLocale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer postgres = PostgresImage.CreateBuilder().Build();
        await postgres.StartAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select datlocprovider::text, datlocale from pg_database where datname = current_database()", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        Assert.Equal("i", reader.GetString(0));
        Assert.Equal(PostgresImage.Locale, reader.GetFieldValue<string?>(1));
    }

    [Fact]
    public void TheComposeFileStartsTheSameImageWithTheSameInitArguments()
    {
        var compose = File.ReadAllText(ComposeFilePath());

        Assert.Equal(PostgresImage.Name, SingleValue(ImageLine(), compose));
        Assert.Equal(PostgresImage.InitDbArgs, SingleValue(InitDbArgsLine(), compose));
    }

    private static string SingleValue(Regex line, string compose) =>
        Assert.Single(line.Matches(compose)).Groups["value"].Value;

    private static string ComposeFilePath() =>
        typeof(PostgresImageTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "ComposeFilePath")
            .Value!;

    [GeneratedRegex("""^\s*image:\s*(?<value>\S+)\s*$""", RegexOptions.Multiline)]
    private static partial Regex ImageLine();

    [GeneratedRegex("""^\s*POSTGRES_INITDB_ARGS:\s*"(?<value>[^"]*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex InitDbArgsLine();
}
