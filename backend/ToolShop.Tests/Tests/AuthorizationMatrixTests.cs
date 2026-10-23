using System.Net;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// T2b step 1: records WHO may call WHICH endpoint today (A=Admin, M=Manager, C=Cashier, W=Warehouse, X=anonymous).
/// "May call" means the response is neither 401 nor 403. Requests use placeholder ids and an empty JSON body,
/// so they fail validation or return 404 without changing data. The RBAC refactor must keep this table identical.
/// </summary>
[Collection("db")]
public class AuthorizationMatrixTests(ApiFactory factory) : TestBase(factory)
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["POST api/auth/login"] = "XAMCW",
        ["POST api/auth/refresh"] = "XAMCW",
        ["GET api/auth/me"] = "AMCW",
        ["POST api/auth/logout"] = "XAMCW",
        ["POST api/auth/mfa/setup"] = "XAMCW",
        ["POST api/auth/mfa/enable"] = "XAMCW",
        ["POST api/auth/mfa/verify"] = "XAMCW",
        ["POST api/auth/mfa/self/setup"] = "AMCW",
        ["POST api/auth/mfa/self/enable"] = "AMCW",
        ["POST api/auth/mfa/self/disable"] = "AMCW",
        ["POST api/auth/mfa/self/recovery-codes"] = "AMCW",
        ["GET api/brands"] = "AMCW",
        ["GET api/brands/{id:int}"] = "AMCW",
        ["POST api/brands"] = "AM",
        ["PUT api/brands/{id:int}"] = "AM",
        ["DELETE api/brands/{id:int}"] = "AM",
        ["GET api/categories"] = "AMCW",
        ["GET api/categories/{id:int}"] = "AMCW",
        ["POST api/categories"] = "AM",
        ["PUT api/categories/{id:int}"] = "AM",
        ["DELETE api/categories/{id:int}"] = "AM",
        ["GET api/customers"] = "AMC",
        ["GET api/customers/{id:int}"] = "AMC",
        ["POST api/customers"] = "AMC",
        ["PUT api/customers/{id:int}"] = "AMC",
        ["DELETE api/customers/{id:int}"] = "AM",
        ["GET api/customers/{id:int}/orders"] = "AMC",
        ["GET api/customers/{id:int}/payments"] = "AMC",
        ["GET api/customers/{id:int}/outstanding-orders"] = "AMC",
        ["GET api/dashboard/summary"] = "AM",
        ["GET api/dashboard/sales-trend"] = "AM",
        ["GET api/dashboard/sales-by-category"] = "AM",
        ["GET api/dashboard/attention"] = "AM",
        ["GET api/dashboard/low-stock"] = "AM",
        ["GET api/dashboard/financial-summary"] = "AM",
        ["GET api/dashboard/top-products"] = "AM",
        ["GET api/expense-categories"] = "AM",
        ["POST api/expense-categories"] = "AM",
        ["PUT api/expense-categories/{id:int}"] = "AM",
        ["DELETE api/expense-categories/{id:int}"] = "AM",
        ["GET api/expenses"] = "AM",
        ["GET api/expenses/{id:int}"] = "AM",
        ["POST api/expenses"] = "AM",
        ["PUT api/expenses/{id:int}"] = "AM",
        ["POST api/expenses/{id:int}/void"] = "A",
        ["GET api/finance/summary"] = "AM",
        ["GET api/finance/daily"] = "AM",
        ["GET api/finance/monthly"] = "AM",
        ["GET api/finance/expenses-by-category"] = "AM",
        ["GET api/finance/payments-by-method"] = "AM",
        ["GET api/finance/profit-by-product"] = "AM",
        ["GET api/finance/profit-by-category"] = "AM",
        ["GET api/Health"] = "XAMCW",
        ["POST api/inventory/stock-in"] = "AMW",
        ["POST api/inventory/stock-out"] = "AMW",
        ["POST api/inventory/adjustment"] = "AMW",
        ["GET api/inventory/stock"] = "AMCW",
        ["GET api/inventory/stock/{productId:int}"] = "AMCW",
        ["GET api/inventory/low-stock"] = "AMCW",
        ["GET api/inventory/out-of-stock"] = "AMCW",
        ["GET api/inventory/movements"] = "AMW",
        ["GET api/inventory/products/{productId:int}/movements"] = "AMW",
        ["GET api/orders"] = "AMC",
        ["GET api/orders/{id:int}"] = "AMC",
        ["POST api/orders"] = "AMC",
        ["PUT api/orders/{id:int}"] = "AMC",
        ["POST api/orders/{id:int}/complete"] = "AMC",
        ["POST api/orders/{id:int}/cancel"] = "AMC",
        ["POST api/orders/{id:int}/return"] = "AM",
        ["GET api/orders/{id:int}/payments"] = "AMC",
        ["GET api/payments"] = "AMC",
        ["GET api/payments/{id:int}"] = "AMC",
        ["POST api/payments"] = "AMC",
        ["POST api/payments/{id:int}/void"] = "AM",
        ["GET api/debts"] = "AMC",
        ["GET api/debts/walk-in-exceptions"] = "AM",
        ["GET api/products"] = "AMCW",
        ["GET api/products/{id:int}"] = "AMCW",
        ["POST api/products"] = "AM",
        ["PUT api/products/{id:int}"] = "AM",
        ["DELETE api/products/{id:int}"] = "AM",
        ["GET api/purchases"] = "AMW",
        ["GET api/purchases/{id:int}"] = "AMW",
        ["POST api/purchases"] = "AMW",
        ["PUT api/purchases/{id:int}"] = "AMW",
        ["POST api/purchases/{id:int}/confirm"] = "AM",
        ["POST api/purchases/{id:int}/cancel"] = "AMW",
        ["GET api/reports/sales"] = "AM",
        ["GET api/reports/sales/summary"] = "AM",
        ["GET api/reports/sales/export"] = "AM",
        ["GET api/reports/returns"] = "AM",
        ["GET api/reports/inventory"] = "AM",
        ["GET api/reports/inventory/export"] = "AM",
        ["GET api/reports/customer-debt"] = "AM",
        ["GET api/reports/customer-debt/export"] = "AM",
        ["GET api/reports/purchases"] = "AM",
        ["GET api/reports/purchases/export"] = "AM",
        ["GET api/reports/expenses"] = "AM",
        ["GET api/reports/expenses/export"] = "AM",
        ["GET api/reports/payments"] = "AM",
        ["GET api/reports/payments/export"] = "AM",
        ["GET api/reports/product-performance"] = "AM",
        ["GET api/reports/category-performance"] = "AM",
        ["GET api/reports/profitability"] = "AM",
        ["GET api/suppliers"] = "AMW",
        ["GET api/suppliers/{id:int}"] = "AMW",
        ["GET api/suppliers/{id:int}/purchases"] = "AMW",
        ["POST api/suppliers"] = "AMW",
        ["PUT api/suppliers/{id:int}"] = "AMW",
        ["DELETE api/suppliers/{id:int}"] = "AM",
        ["GET api/users"] = "A",
        ["POST api/users"] = "A",
        ["PUT api/users/{id}"] = "A",
        ["POST api/users/{id}/deactivate"] = "A",
        ["POST api/users/{id}/activate"] = "A",
        ["POST api/users/{id}/reset-password"] = "A",
        ["POST api/users/{id}/mfa/reset"] = "A",
    };

    private static readonly System.Text.RegularExpressions.Regex Param =
        new(@"\{\w+(:[^}]*)?\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private List<(string Method, string Template)> ApiEndpoints()
    {
        var source = Factory.Services.GetRequiredService<EndpointDataSource>();
        var list = new List<(string, string)>();
        foreach (var e in source.Endpoints.OfType<RouteEndpoint>())
        {
            var raw = e.RoutePattern.RawText ?? string.Empty;
            if (!raw.StartsWith("api/", StringComparison.OrdinalIgnoreCase)) continue;
            var methods = e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var m in methods) list.Add((m.ToUpperInvariant(), raw));
        }
        return list.Distinct().OrderBy(x => x.Item2).ThenBy(x => x.Item1).ToList();
    }

    [Fact]
    public void Matrix_lists_every_api_endpoint_and_nothing_else()
    {
        var actual = ApiEndpoints().Select(e => $"{e.Method} {e.Template}").ToHashSet();
        var missing = actual.Except(Expected.Keys).OrderBy(x => x).ToList();
        var stale = Expected.Keys.Except(actual).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0 && stale.Count == 0,
            "Not in matrix: [" + string.Join("; ", missing) + "]  Stale in matrix: [" + string.Join("; ", stale) + "]");
    }

    [Fact]
    public async Task Effective_access_of_every_endpoint_matches_the_recorded_matrix()
    {
        var principals = new (char Code, HttpClient Client)[]
        {
            ('X', Anonymous), ('A', Admin), ('M', Manager), ('C', Cashier), ('W', Warehouse)
        };

        var problems = new List<string>();
        foreach (var (method, template) in ApiEndpoints())
        {
            var key = $"{method} {template}";
            if (!Expected.TryGetValue(key, out var allowedCodes)) continue; // reported by the other test

            var url = "/" + Param.Replace(template, "999999");
            var actual = new StringBuilder();
            foreach (var (code, client) in principals)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (method != "GET")
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request);
                var allowed = response.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden;
                if (allowed) actual.Append(code);
            }

            var want = string.Concat("XAMCW".Where(allowedCodes.Contains));
            if (actual.ToString() != want)
                problems.Add($"{key}: expected '{want}' but got '{actual}'");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
