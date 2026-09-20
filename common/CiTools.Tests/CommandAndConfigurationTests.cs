namespace CiTools.Tests;

public sealed class CommandAndConfigurationTests {
    [TestCase("steam-buildfile")]
    [TestCase("steam-login")]
    public void RoutesPublicCommandsThroughComposer(string executable) {
        string[] arguments = ["argument with spaces", "--option=value", string.Empty];

        string[] command = CommandRouter.Route(executable, arguments);

        string[] expected = [
            "exec",
            executable,
            "--",
            "argument with spaces",
            "--option=value",
            string.Empty
        ];
        Assert.That(command, Is.EqualTo(expected));
    }

    [Test]
    public void RejectsUnknownLauncherNames() {
        var exception = Assert.Throws<ArgumentException>(() => CommandRouter.Route("ci-tools-launcher", []));

        Assert.That(exception!.Message, Does.Contain("ci-tools-launcher"));
    }

    [Test]
    public void UsesInvokedAliasInsteadOfResolvedProcessPath() {
        string executable = Program.ResolveExecutable(
            "/ci-tools/ci-tools-launcher",
            ["/ci-tools/ci-tools-launcher", "argument"],
            "/usr/local/bin/steam-buildfile");

        Assert.That(executable, Is.EqualTo("steam-buildfile"));
    }

    [Test]
    public void SelectsInstalledComposerProjectWithoutChangingWorkingDirectory() {
        string[] command = ["exec", "steam-buildfile", "--", "argument with spaces"];
        var startInfo = Program.ComposerStartInfo(command);

        using (Assert.EnterMultipleScope()) {
            Assert.That(startInfo.Environment["COMPOSER"], Is.EqualTo(OperatingSystem.IsWindows()
                ? @"C:\ci-tools\composer.json"
                : "/ci-tools/composer.json"));
            Assert.That(startInfo.Environment["COMPOSER_VENDOR_DIR"], Is.EqualTo(OperatingSystem.IsWindows()
                ? @"C:\ci-tools\vendor"
                : "/ci-tools/vendor"));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(Environment.CurrentDirectory));
            Assert.That(startInfo.ArgumentList.TakeLast(command.Length), Is.EqualTo(command));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.RedirectStandardInput, Is.False);
            Assert.That(startInfo.RedirectStandardOutput, Is.False);
            Assert.That(startInfo.RedirectStandardError, Is.False);
        }
    }

    [TestCase(null, 86_400)]
    [TestCase("", 86_400)]
    [TestCase("0", 0)]
    [TestCase("42", 42)]
    public void ParsesCallTimeout(string? value, long expected) =>
        Assert.That(Program.ParseTimeout(value), Is.EqualTo(expected));

    [TestCase("-1")]
    [TestCase("one")]
    [TestCase(" 1")]
    public void RejectsInvalidCallTimeout(string value) =>
        Assert.Throws<ArgumentException>(() => Program.ParseTimeout(value));
}
