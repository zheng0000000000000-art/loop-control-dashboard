// 제안 흐름에서 공유하는 측정 적용 컨텍스트를 정의한다.
// dispatch 리팩터링 산출물이 사용하는 데이터 묶음이다.
using System.Text.Json.Nodes;

public sealed class MeasurementApplyContext
{
    public required List<MetricCheck> Checks { get; init; }
    public required List<MetricCheck> Violations { get; init; }
    public required List<MetricCheck> PreviousChecks { get; init; }
    public required List<MetricRegression> Regressions { get; init; }
    public required MeasurementStages Stages { get; init; }
    public required JsonObject State { get; set; }
    public required JsonObject RunLog { get; set; }
}
