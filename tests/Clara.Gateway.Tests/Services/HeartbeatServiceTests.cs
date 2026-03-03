using Clara.Gateway.Services;

namespace Clara.Gateway.Tests.Services;

public class HeartbeatServiceTests
{
    [Fact]
    public void ParseChecklistItems_extracts_unchecked_items()
    {
        var markdown = """
            # Heartbeat Checklist

            - [ ] Check memory system health
            - [ ] Review pending notifications
            """;

        var items = HeartbeatService.ParseChecklistItems(markdown);

        Assert.Equal(2, items.Count);
        Assert.Equal("Check memory system health", items[0]);
        Assert.Equal("Review pending notifications", items[1]);
    }

    [Fact]
    public void ParseChecklistItems_extracts_checked_items()
    {
        var markdown = """
            - [x] Completed task
            - [ ] Pending task
            """;

        var items = HeartbeatService.ParseChecklistItems(markdown);

        Assert.Equal(2, items.Count);
        Assert.Equal("Completed task", items[0]);
        Assert.Equal("Pending task", items[1]);
    }

    [Fact]
    public void ParseChecklistItems_empty_markdown_returns_empty_list()
    {
        var items = HeartbeatService.ParseChecklistItems("");

        Assert.Empty(items);
    }

    [Fact]
    public void ParseChecklistItems_no_checklist_items_returns_empty_list()
    {
        var markdown = """
            # Heartbeat

            This is just regular text.
            - Regular bullet point
            * Another bullet
            """;

        var items = HeartbeatService.ParseChecklistItems(markdown);

        Assert.Empty(items);
    }

    [Fact]
    public void ParseChecklistItems_mixed_content_extracts_only_items()
    {
        var markdown = """
            # Daily Heartbeat

            Some intro text here.

            ## Tasks
            - [ ] Check database connections
            - Regular list item (not a checklist)
            - [x] Verify API endpoints

            ## Notes
            More text that should be ignored.
            - [ ] Send daily summary
            """;

        var items = HeartbeatService.ParseChecklistItems(markdown);

        Assert.Equal(3, items.Count);
        Assert.Equal("Check database connections", items[0]);
        Assert.Equal("Verify API endpoints", items[1]);
        Assert.Equal("Send daily summary", items[2]);
    }

    [Fact]
    public void ParseChecklistItems_ignores_empty_checklist_items()
    {
        var markdown = """
            - [ ]
            - [ ] Valid item
            - [x]
            """;

        var items = HeartbeatService.ParseChecklistItems(markdown);

        Assert.Single(items);
        Assert.Equal("Valid item", items[0]);
    }

    [Fact]
    public void ParseChecklistItems_handles_leading_whitespace()
    {
        var markdown = "  - [ ] Indented item\n    - [ ] More indented";

        var items = HeartbeatService.ParseChecklistItems(markdown);

        Assert.Equal(2, items.Count);
        Assert.Equal("Indented item", items[0]);
        Assert.Equal("More indented", items[1]);
    }
}
