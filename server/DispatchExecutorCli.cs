// 격리 사본 안에서 실행자 명령을 재현한다.
// dispatch 검증용 파일 변경과 시간 초과 경로를 만든다.
using System.Text;

public static class DispatchExecutorCli
{
    private const char OpenBrace = (char)123;
    private const char CloseBrace = (char)125;

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

        if (IsSelfRefactorInstruction(instruction))
        {
            if (!executor.Equals("claude-code", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("self-refactor rule is unavailable for this executor");
                return 1;
            }

            ApplySelfRefactor();
            Console.WriteLine("self-refactor templates applied");
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

    // 자기 리팩터링 지시인지 확인한다.
    private static bool IsSelfRefactorInstruction(string instruction)
    {
        return instruction.Contains("Program.cs", StringComparison.OrdinalIgnoreCase) &&
            instruction.Contains("Orchestrator.cs", StringComparison.OrdinalIgnoreCase) &&
            instruction.Contains("ProposalFlow.cs", StringComparison.OrdinalIgnoreCase);
    }

    // 자기 리팩터링 템플릿을 격리 사본에 적용한다.
    private static void ApplySelfRefactor()
    {
        var root = FindWorkspaceRoot();
        var programPath = Path.Combine(root, "server", "Program.cs");
        var enginePath = Path.Combine(root, "server", "Engine.cs");
        var tunerPath = Path.Combine(root, "server", "BalanceTuner.cs");
        var appPath = Path.Combine(root, "dashboard", "app.js");
        var programText = File.ReadAllText(programPath, Encoding.UTF8);
        var engineText = File.ReadAllText(enginePath, Encoding.UTF8);
        var tunerText = File.ReadAllText(tunerPath, Encoding.UTF8);
        var appText = File.ReadAllText(appPath, Encoding.UTF8);
        File.WriteAllText(programPath, ReplaceFunction(programText, "static int ApplyMeasurementResult(", ReadTemplate(root, "ApplyMeasurementResult.txt")), Encoding.UTF8);
        File.WriteAllText(enginePath, ReplaceFunction(engineText, "public static JsonObject ApplyStatePatch(", ReadTemplate(root, "EngineApplyStatePatch.txt")), Encoding.UTF8);
        File.WriteAllText(tunerPath, ReplaceFunction(tunerText, "public static TuningResult Search(", ReadTemplate(root, "BalanceTunerSearch.txt")), Encoding.UTF8);
        appText = ConvertFunctionToConst(appText, "renderStageDetail");
        appText = ConvertFunctionToConst(appText, "renderApprovalPanel");
        appText = ConvertFunctionToConst(appText, "renderProposalChange");
        File.WriteAllText(appPath, appText, Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "server", "ProposalFlow.cs"), ReadTemplate(root, "ProposalFlow.txt"), Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "server", "Orchestrator.cs"), ReadTemplate(root, "Orchestrator.txt"), Encoding.UTF8);
    }

    // dispatch 템플릿 파일을 읽는다.
    private static string ReadTemplate(string root, string name)
    {
        return File.ReadAllText(Path.Combine(root, "server", "dispatch-templates", name), Encoding.UTF8).TrimEnd() + Environment.NewLine;
    }

    // 지정한 함수 본문을 중괄호 깊이 기준으로 교체한다.
    private static string ReplaceFunction(string text, string signatureStart, string replacement)
    {
        var start = text.IndexOf(signatureStart, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException(signatureStart + " not found");
        }

        var open = text.IndexOf(OpenBrace, start);
        var end = FindBalancedEnd(text, open);
        var lineEnd = SkipTrailingLineBreaks(text, end + 1);
        return text[..start] + replacement + text[lineEnd..];
    }

    // JS 함수 선언을 const 함수 표현식으로 바꾼다.
    private static string ConvertFunctionToConst(string text, string functionName)
    {
        var signature = "function " + functionName + "(";
        var start = text.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            return text;
        }

        var open = text.IndexOf(OpenBrace, start);
        var args = text[(start + signature.Length)..text.LastIndexOf(')', open)];
        var prefix = "const " + functionName + " = (" + args + ") => ";
        var converted = text[..start] + prefix + text[open..];
        var convertedOpen = text[..start].Length + prefix.Length;
        var end = FindFunctionEnd(converted, convertedOpen);
        return converted[..end] + "};" + converted[(end + 1)..];
    }

    // JS 함수 본문의 닫는 중괄호 위치를 찾는다.
    private static int FindFunctionEnd(string text, int open)
    {
        return FindBalancedEnd(text, open);
    }

    // 중괄호 균형이 끝나는 위치를 찾는다.
    private static int FindBalancedEnd(string text, int open)
    {
        var depth = 0;
        for (var index = open; index < text.Length; index += 1)
        {
            if (text[index] == OpenBrace)
            {
                depth += 1;
            }
            else if (text[index] == CloseBrace)
            {
                depth -= 1;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        throw new InvalidOperationException("balanced end not found");
    }

    // 줄 끝 개행 문자를 건너뛴다.
    private static int SkipTrailingLineBreaks(string text, int start)
    {
        var index = start;
        while (index < text.Length && (text[index] == '\r' || text[index] == '\n'))
        {
            index += 1;
        }

        return index;
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
