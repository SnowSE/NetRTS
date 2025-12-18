using BoDi;
using Reqnroll;

namespace NetRts.ContractTests.Support;

[Binding]
public class Hooks
{
    private static ApiWebApplicationFactory? _factory;

    [BeforeTestRun]
    public static void BeforeTestRun()
    {
        _factory = new ApiWebApplicationFactory();
    }

    [AfterTestRun]
    public static void AfterTestRun()
    {
        _factory?.Dispose();
    }

    [BeforeScenario]
    public void BeforeScenario(IObjectContainer container)
    {
        if (_factory == null)
        {
            throw new InvalidOperationException("Factory not initialized");
        }

        // Register factory in container
        container.RegisterInstanceAs(_factory);

        // Create and register TestContext
        var testContext = new TestContext
        {
            HttpClient = _factory.CreateClient()
        };
        testContext.HttpClient.BaseAddress = new Uri("https://localhost");

        container.RegisterInstanceAs(testContext);
    }
}
