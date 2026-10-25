using System.Runtime.InteropServices;

namespace Workshop.Tests;

[TestClass]
public sealed class CommandLineArgumentsTests
{
    private static readonly string[] Awkward =
    [
        "--seed=12345",
        "--setting=Ui.Title=Two words",
        "",
        "say \"hi\"",
        @"C:\Program Files\Game\",
        @"back\slash",
        @"ends with \\",
        "\ttab",
    ];

    [TestMethod]
    public void JoinedArguments_SplitBackAsTheSameArguments()
    {
        CollectionAssert.AreEqual(Awkward, CommandLineArguments.Split(CommandLineArguments.Join(Awkward)).ToArray());
    }

    [TestMethod]
    public void JoinedArguments_AreReadByWindowsAsTheSameArguments()
    {
        CollectionAssert.AreEqual(Awkward, WindowsSplit(CommandLineArguments.Join(Awkward)));
    }

    [TestMethod]
    public void AnArgumentWithNothingToQuote_IsWrittenAsItIs()
    {
        Assert.AreEqual("--seed=1 --headless", CommandLineArguments.Join(["--seed=1", "--headless"]));
        Assert.AreEqual("\"--setting=Ui.Title=Two words\"", CommandLineArguments.Quote("--setting=Ui.Title=Two words"));
    }

    [TestMethod]
    public void Split_ReadsTypedTextTheWayWindowsDoes()
    {
        const string typed = "  --map-size=512 \"--setting=A.B=x y\"   say\\\"hi --flag";

        CollectionAssert.AreEqual(WindowsSplit(typed), CommandLineArguments.Split(typed).ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), CommandLineArguments.Split("   ").ToArray());
    }

    /// <summary>shell32's CommandLineToArgvW over a program name and commandLine, the program name left out.</summary>
    private static string[] WindowsSplit(string commandLine)
    {
        var pointer = CommandLineToArgvW($"Game.exe {commandLine}", out var count);
        try
        {
            var arguments = new string[count - 1];
            for (var index = 1; index < count; index++)
            {
                arguments[index - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size))!;
            }

            return arguments;
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
