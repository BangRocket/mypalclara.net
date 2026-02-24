using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Data;
using MyPalClara.Data.Entities;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class PersonalityTools
{
    public static void Register(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("update_personality", new ToolSchema("update_personality",
            "Add, update, or remove a personality trait. Args: action (string: add|update|remove), category (string), trait_key (string), content (string, optional), reason (string, optional).",
            JsonDocument.Parse("""
            {"type":"object","properties":{"action":{"type":"string","enum":["add","update","remove"]},"category":{"type":"string"},"trait_key":{"type":"string"},"content":{"type":"string"},"reason":{"type":"string"}},"required":["action","category","trait_key"]}
            """).RootElement),
            (args, ctx, ct) => UpdatePersonalityAsync(args, ctx, scopeFactory, ct));
    }

    public static async Task<ToolResult> UpdatePersonalityAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        if (!args.TryGetValue("action", out var actionElem))
            return new ToolResult(false, "", "Missing: action");
        if (!args.TryGetValue("category", out var catElem))
            return new ToolResult(false, "", "Missing: category");
        if (!args.TryGetValue("trait_key", out var keyElem))
            return new ToolResult(false, "", "Missing: trait_key");

        var action = actionElem.GetString()!;
        var category = catElem.GetString()!;
        var traitKey = keyElem.GetString()!;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var existing = await db.PersonalityTraits
            .FirstOrDefaultAsync(t => t.Category == category && t.TraitKey == traitKey, ct);

        switch (action)
        {
            case "add":
                if (!args.TryGetValue("content", out var contentElem))
                    return new ToolResult(false, "", "Missing: content for add action");

                if (existing is not null)
                    return new ToolResult(false, "", $"Trait '{category}/{traitKey}' already exists. Use update.");

                db.PersonalityTraits.Add(new PersonalityTrait
                {
                    Id = Guid.NewGuid().ToString(),
                    Category = category,
                    TraitKey = traitKey,
                    Content = contentElem.GetString()!,
                    Reason = args.TryGetValue("reason", out var rElem) ? rElem.GetString() : null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);
                return new ToolResult(true, $"Added trait: {category}/{traitKey}");

            case "update":
                if (existing is null)
                    return new ToolResult(false, "", $"Trait '{category}/{traitKey}' not found.");
                if (args.TryGetValue("content", out var cElem))
                    existing.Content = cElem.GetString()!;
                if (args.TryGetValue("reason", out var rrElem))
                    existing.Reason = rrElem.GetString();
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return new ToolResult(true, $"Updated trait: {category}/{traitKey}");

            case "remove":
                if (existing is null)
                    return new ToolResult(false, "", $"Trait '{category}/{traitKey}' not found.");
                existing.Active = false;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return new ToolResult(true, $"Removed trait: {category}/{traitKey}");

            default:
                return new ToolResult(false, "", $"Unknown action: {action}. Use add, update, or remove.");
        }
    }
}
