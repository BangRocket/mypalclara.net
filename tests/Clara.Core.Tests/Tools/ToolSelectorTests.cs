using Clara.Core.Tools;

namespace Clara.Core.Tests.Tools;

public class ToolSelectorTests
{
    private readonly ToolSelector _selector = new();

    [Fact]
    public void Read_the_file_maps_to_FileSystem()
    {
        var categories = _selector.SelectCategories("Read the file");

        Assert.Contains(ToolCategory.FileSystem, categories);
    }

    [Fact]
    public void Search_the_web_maps_to_Web()
    {
        var categories = _selector.SelectCategories("Search the web for information");

        Assert.Contains(ToolCategory.Web, categories);
    }

    [Fact]
    public void Run_this_code_maps_to_Shell_and_CodeExecution()
    {
        var categories = _selector.SelectCategories("Run this code");

        Assert.Contains(ToolCategory.Shell, categories);
        Assert.Contains(ToolCategory.CodeExecution, categories);
    }

    [Fact]
    public void Unknown_message_returns_all_categories()
    {
        var categories = _selector.SelectCategories("Hello, how are you today?");

        var allCategories = Enum.GetValues<ToolCategory>();
        Assert.Equal(allCategories.Length, categories.Count);
    }

    [Fact]
    public void GitHub_keywords_map_correctly()
    {
        Assert.Contains(ToolCategory.GitHub, _selector.SelectCategories("Check the github repo"));
        Assert.Contains(ToolCategory.GitHub, _selector.SelectCategories("Open a pull request"));
        Assert.Contains(ToolCategory.GitHub, _selector.SelectCategories("Look at the issue"));
    }

    [Fact]
    public void Memory_keywords_map_correctly()
    {
        Assert.Contains(ToolCategory.Memory, _selector.SelectCategories("Remember this fact"));
        Assert.Contains(ToolCategory.Memory, _selector.SelectCategories("Forget that information"));
        Assert.Contains(ToolCategory.Memory, _selector.SelectCategories("Search my memory"));
    }

    [Fact]
    public void Case_insensitive_matching()
    {
        var categories = _selector.SelectCategories("SEARCH THE WEB");

        Assert.Contains(ToolCategory.Web, categories);
    }
}
