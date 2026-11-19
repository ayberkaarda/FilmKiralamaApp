using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace FilmKiralama.UI.Tests;

public sealed class ConfigurationTests
{
    private static string ExamplePath => Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");

    [Fact]
    public void ExampleSettings_AreValidJsonWithALocalServerAndDemoOff()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ExamplePath));

        var configuration = new ConfigurationBuilder().AddSettingsFile(AppContext.BaseDirectory, "appsettings.example.json").Build();
        var connection = AppConfiguration.ResolveConnectionString(configuration);

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.NotNull(connection);
        Assert.StartsWith("Server=localhost\\SQLEXPRESS;", connection, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", connection, StringComparison.OrdinalIgnoreCase);
        Assert.False(AppConfiguration.IsDemoRequested(configuration));

        var (policy, problems) = AppConfiguration.ReadRentalPolicy(configuration);
        Assert.Empty(problems);
        Assert.Equal(10m, policy.DailyLateFee);
        Assert.Equal(365, policy.MaxRentalDays);
    }

    [Fact]
    public void ConnectionString_EnvironmentVariableWins()
    {
        var configuration = Settings(new()
        {
            ["ConnectionStrings:FilmKiralama"] = "Server=dosya;",
            ["FILM_CONNECTION"] = "Server=ortam;",
        });

        Assert.Equal("Server=ortam;", AppConfiguration.ResolveConnectionString(configuration));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("0", false)]
    [InlineData("evet", false)]
    [InlineData(null, false)]
    public void Demo_IsOnOnlyWhenAskedFor(string? value, bool expected)
    {
        Assert.Equal(expected, AppConfiguration.IsDemoRequested(Settings(new() { ["FILM_DEMO"] = value })));
    }

    [Theory]
    [InlineData("abc", "30")]
    [InlineData("-1", "30")]
    [InlineData("10", "0")]
    [InlineData("10", "otuz")]
    [InlineData("10.555", "30")]
    public void RentalSettings_InvalidValues_AreReportedAndTheDefaultsKept(string fee, string days)
    {
        var (policy, problems) = AppConfiguration.ReadRentalPolicy(Settings(new()
        {
            ["Rental:DailyLateFee"] = fee,
            ["Rental:MaxRentalDays"] = days,
        }));

        Assert.NotEmpty(problems);
        Assert.Equal(10m, policy.DailyLateFee);
        Assert.Equal(365, policy.MaxRentalDays);
    }

    [Fact]
    public void RentalSettings_AreReadWithTheInvariantCulture()
    {
        var (policy, problems) = AppConfiguration.ReadRentalPolicy(Settings(new()
        {
            ["Rental:DailyLateFee"] = "7.25",
            ["Rental:MaxRentalDays"] = "14",
        }));

        Assert.Empty(problems);
        Assert.Equal(7.25m, policy.DailyLateFee);
        Assert.Equal(14, policy.MaxRentalDays);
    }

    private static IConfiguration Settings(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
