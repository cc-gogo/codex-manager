using CodexConversationManager.App.Services;
using Xunit;

namespace CodexConversationManager.Tests.ViewModels;

public sealed class CodexHomeResolverTests
{
    [Fact]
    public void Explicit_absolute_command_line_path_takes_precedence_over_saved_setting()
    {
        var result = CodexHomeResolver.Resolve(
            ["--codex-home", "D:\\fixture"],
            new AppSettings("E:\\saved"),
            "C:\\Users\\ASUS");

        Assert.Equal(Path.GetFullPath("D:\\fixture"), result);
    }

    [Fact]
    public void Saved_absolute_path_is_used_when_no_command_line_override_exists()
    {
        var result = CodexHomeResolver.Resolve([], new AppSettings("E:\\saved"), "C:\\Users\\ASUS");

        Assert.Equal(Path.GetFullPath("E:\\saved"), result);
    }

    [Fact]
    public void Default_path_is_user_profile_codex()
    {
        var result = CodexHomeResolver.Resolve([], new AppSettings(), "C:\\Users\\ASUS");

        Assert.Equal(Path.Combine("C:\\Users\\ASUS", ".codex"), result);
    }
}
