// 격리 사본 안에서 실행자 명령을 재현한다.
// dispatch 검증용 파일 변경과 시간 초과 경로를 만든다.
using System.Text;

public static class DispatchExecutorCli
{
    // 격리 실행자 CLI를 실행한다.
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: dispatch-executor <executor> <instruction>");
            return 2;
        }

        var executor = args[1];
        var instruction = args[2];
        Console.WriteLine($"executor={executor}");

        if (instruction.Contains("__timeout__", StringComparison.OrdinalIgnoreCase))
        {
            Thread.Sleep(Timeout.Infinite);
            return 124;
        }

        if (instruction.Contains("README", StringComparison.OrdinalIgnoreCase) &&
            (instruction.Contains("한 줄", StringComparison.OrdinalIgnoreCase) ||
                instruction.Contains("one line", StringComparison.OrdinalIgnoreCase)))
        {
            AppendReadmeLine();
            Console.WriteLine("README.md updated");
            return 0;
        }

        File.WriteAllText("EXECUTOR_REPORT.md", $"No deterministic edit rule matched for {executor}.{Environment.NewLine}", Encoding.UTF8);
        Console.WriteLine("no edit rule matched");
        return 0;
    }

    // README 검증 문구를 중복 없이 추가한다.
    private static void AppendReadmeLine()
    {
        var path = Path.Combine(FindWorkspaceRoot(), "README.md");
        var line = "- Dispatch verification line.";
        var text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "# Loop Control Dashboard" + Environment.NewLine;

        if (text.Contains(line, StringComparison.Ordinal))
        {
            return;
        }

        if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
        {
            text += Environment.NewLine;
        }

        text += line + Environment.NewLine;
        File.WriteAllText(path, text, Encoding.UTF8);
    }

    // 현재 실행 위치에서 저장소 루트를 찾는다.
    private static string FindWorkspaceRoot()
    {
        var current = Directory.GetCurrentDirectory();
        return string.Equals(Path.GetFileName(current), "server", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(current)!.FullName
            : current;
    }
}
