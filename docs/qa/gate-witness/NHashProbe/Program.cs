// 정규화 해시의 네 가지 반증 사례를 제품 프로젝트 복원과 독립적으로 실행한다.
using System.Text;

var utf8 = new UTF8Encoding(false);
var cases = new[]
{
    (Name: "crlf-vs-lf", Left: utf8.GetBytes("alpha\r\nbeta\r\n"),
        Right: utf8.GetBytes("alpha\nbeta\n"), ExpectedEqual: true),
    (Name: "content-change", Left: utf8.GetBytes("alpha\nbeta\n"),
        Right: utf8.GetBytes("alpha\ngamma\n"), ExpectedEqual: false),
    (Name: "trailing-whitespace", Left: utf8.GetBytes("alpha  \nbeta\t\n"),
        Right: utf8.GetBytes("alpha\nbeta\n"), ExpectedEqual: true),
    (Name: "bom-vs-no-bom", Left: new byte[] { 0xEF, 0xBB, 0xBF }.Concat(utf8.GetBytes("alpha\n")).ToArray(),
        Right: utf8.GetBytes("alpha\n"), ExpectedEqual: true),
};

var mismatches = 0;
foreach (var test in cases)
{
    var left = NormalizedContentHash.Compute(test.Left);
    var right = NormalizedContentHash.Compute(test.Right);
    var actualEqual = left == right;
    if (actualEqual != test.ExpectedEqual) mismatches++;
    Console.WriteLine($"{test.Name}: expectedEqual={test.ExpectedEqual} actualEqual={actualEqual}");
}

Console.WriteLine($"caseCount={cases.Length} mismatchCount={mismatches}");
return mismatches == 0 ? 0 : 1;
