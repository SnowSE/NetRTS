using BoDi;
using Reqnroll;

namespace NetRts.ContractTests.Support;

[Binding]
public class Hooks
{
    private static ApiWebApplicationFactory? _factory;
    private readonly ScenarioContext _scenarioContext;

    public Hooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeTestRun]
    public static void BeforeTestRun()
    {
        _factory = new ApiWebApplicationFactory();
        _factory.EnsureDatabaseCreated();
    }

    [AfterTestRun]
    public static void AfterTestRun()
    {
        _factory?.Dispose();
    }

    [BeforeScenario]
    public void BeforeScenario()
    {
        if (_factory == null)
        {
            throw new InvalidOperationException("Factory not initialized");
        }

        var container = _scenarioContext.ScenarioContainer;

        // Register factory in container
        container.RegisterInstanceAs(_factory);

        // Create and register TestContext
        var testContext = new TestContext
        {
            HttpClient = _factory.CreateClient(),
            ServiceProvider = _factory.Services
        };
        testContext.HttpClient.BaseAddress = new Uri("https://localhost");

        container.RegisterInstanceAs(testContext);
    }
}
