using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolShop.Tests.Infrastructure;

namespace ToolShop.Tests.Tests;

/// <summary>
/// Category names in Tajik / Russian / English. Translations are display-only: Name stays the unique main
/// name and the fallback, and ids (CategoryId / ProductId) and reports are not touched.
/// </summary>
[Collection("db")]
public class CategoryTranslationTests(ApiFactory factory) : TestBase(factory)
{
    private static JsonElement Find(JsonElement list, int id) =>
        list.EnumerateArray().First(e => e.GetProperty("id").GetInt32() == id);

    [Fact]
    public async Task Create_stores_trimmed_translations_and_blank_becomes_null()
    {
        var resp = await Admin.PostAsJsonAsync("/api/categories", new
        {
            name = "Асбобҳои барқӣ",
            nameTj = "  Асбобҳои барқӣ  ",
            nameRu = "Электроинструмент",
            nameEn = "   ",
        });
        await resp.ShouldBeAsync(HttpStatusCode.Created);
        var created = await resp.ReadJsonAsync();

        Assert.Equal("Асбобҳои барқӣ", created.Str("name"));
        Assert.Equal("Асбобҳои барқӣ", created.Str("nameTj"));
        Assert.Equal("Электроинструмент", created.Str("nameRu"));
        Assert.Equal(JsonValueKind.Null, created.GetProperty("nameEn").ValueKind);

        var list = await (await Admin.GetAsync("/api/categories")).ReadJsonAsync();
        var row = Find(list, created.Int("id"));
        Assert.Equal("Электроинструмент", row.Str("nameRu"));
        Assert.Equal(JsonValueKind.Null, row.GetProperty("nameEn").ValueKind);
    }

    [Fact]
    public async Task Update_changes_translations_but_keeps_id_name_and_products()
    {
        var productId = await SeedProductAsync();
        var categoryId = await Factory.WithDbAsync(async db =>
            (await db.Products.AsNoTracking().FirstAsync(p => p.Id == productId)).CategoryId);
        var before = await (await Admin.GetAsync($"/api/categories/{categoryId}")).ReadJsonAsync();

        var resp = await Admin.PutAsJsonAsync($"/api/categories/{categoryId}", new
        {
            name = before.Str("name"),
            nameTj = "Тоҷикӣ",
            nameRu = "Русский",
            nameEn = "English",
            description = (string?)null,
            isActive = true,
        });
        await resp.ShouldBeAsync(HttpStatusCode.OK);
        var updated = await resp.ReadJsonAsync();

        Assert.Equal(categoryId, updated.Int("id"));
        Assert.Equal(before.Str("name"), updated.Str("name"));
        Assert.Equal("Русский", updated.Str("nameRu"));
        Assert.Equal("English", updated.Str("nameEn"));

        // the product still belongs to the same category and the list shows its real product count
        var list = await (await Admin.GetAsync("/api/categories")).ReadJsonAsync();
        Assert.Equal(1, Find(list, categoryId).GetProperty("productCount").GetInt32());
        var stillSame = await Factory.WithDbAsync(async db =>
            (await db.Products.AsNoTracking().FirstAsync(p => p.Id == productId)).CategoryId);
        Assert.Equal(categoryId, stillSame);

        // clearing a translation goes back to the fallback (null)
        var cleared = await Admin.PutAsJsonAsync($"/api/categories/{categoryId}", new
        {
            name = before.Str("name"), nameTj = "", nameRu = "", nameEn = "", isActive = true,
        });
        await cleared.ShouldBeAsync(HttpStatusCode.OK);
        var c2 = await cleared.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, c2.GetProperty("nameRu").ValueKind);
    }

    [Fact]
    public async Task Existing_category_without_translations_still_works_and_has_null_translations()
    {
        var productId = await SeedProductAsync();
        var categoryId = await Factory.WithDbAsync(async db =>
            (await db.Products.AsNoTracking().FirstAsync(p => p.Id == productId)).CategoryId);

        var list = await (await Admin.GetAsync("/api/categories")).ReadJsonAsync();
        var row = Find(list, categoryId);
        Assert.False(string.IsNullOrWhiteSpace(row.Str("name")));
        Assert.Equal(JsonValueKind.Null, row.GetProperty("nameTj").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("nameRu").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("nameEn").ValueKind);
    }

    [Fact]
    public async Task Duplicate_main_name_is_still_rejected_and_cashier_cannot_edit()
    {
        await (await Admin.PostAsJsonAsync("/api/categories", new { name = "Дубл" })).ShouldBeAsync(HttpStatusCode.Created);
        await (await Admin.PostAsJsonAsync("/api/categories", new { name = "Дубл", nameRu = "Дубль" })).ShouldBeAsync(HttpStatusCode.Conflict);
        await (await Cashier.PostAsJsonAsync("/api/categories", new { name = "X", nameEn = "X" })).ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Too_long_translation_is_rejected()
    {
        var resp = await Admin.PostAsJsonAsync("/api/categories", new { name = "Long", nameEn = new string('a', 151) });
        await resp.ShouldBeAsync(HttpStatusCode.BadRequest);
    }
}
