using System.Drawing.Imaging;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;
var root = Path.GetFullPath(".");
var source = Path.Combine(root, "artifacts", "Promo", "source");
using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "data.json")));
var clears = data.RootElement.GetProperty("Income").EnumerateArray()
    .Where(record => SchedulerBossHistory.BossKey(record.GetProperty("Name").GetString()!) != SchedulerBossHistory.BossKey("검은 마법사"))
    .Where(record => record.GetProperty("Meso").ValueKind == JsonValueKind.Number)
    .Select(record => new ReplayClear(DateOnly.Parse(record.GetProperty("Date").GetString()!), record.GetProperty("Name").GetString()!, record.GetProperty("Meso").GetInt64())).ToArray();
var demoIndex = Array.IndexOf(args, "--demo-total");
var demo = demoIndex >= 0;
if (demo)
{
    var target = long.Parse(args[demoIndex + 1], System.Globalization.CultureInfo.InvariantCulture);
    var today = new DateOnly(2026, 10, 8);
    var top = ManualWeeklyHistory.Choices.Select(price => CrystalPrices.Find(price.Name, price.Difficulty, today)!)
        .GroupBy(price => SchedulerBossHistory.BossKey(price.Name))
        .Select(group => group.MaxBy(price => price.Meso)!).OrderByDescending(price => price.Meso).Take(12).ToArray();
    var fullSets = target / top.Sum(price => price.Meso);
    long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return a; }
    var unit = top.Select(price => price.Meso).Aggregate(Gcd);
    var remaining = target - fullSets * top.Sum(price => price.Meso);
    if (remaining % unit != 0) throw new InvalidOperationException("Target is not divisible by the price catalog's unit.");
    var size = checked((int)(remaining / unit));
    var counts = top.Select(_ => (int)fullSets).ToArray();
    var dp = Enumerable.Repeat(int.MaxValue / 2, size + 1).ToArray();
    var picked = Enumerable.Repeat(-1, size + 1).ToArray(); dp[0] = 0;
    for (var amount = 1; amount <= size; amount++)
        for (var i = 0; i < top.Length; i++)
        {
            var coin = (int)(top[i].Meso / unit);
            if (coin <= amount && dp[amount - coin] + 1 < dp[amount]) { dp[amount] = dp[amount - coin] + 1; picked[amount] = i; }
        }
    for (var amount = size; amount > 0;)
    {
        var i = picked[amount]; if (i < 0) throw new InvalidOperationException("No exact whole-kill total exists.");
        counts[i]++; amount -= (int)(top[i].Meso / unit);
    }
    var dates = ManualWeeklyHistory.Dates(SchedulerBossHistory.FirstDate, today, today);
    clears = top.SelectMany((price, i) => Enumerable.Range(0, counts[i])
        .Select(kill => new ReplayClear(dates[kill % dates.Count], price.Name, price.Meso))).ToArray();
    if (clears.Sum(clear => clear.Meso) != target || top.Length != 12) throw new InvalidOperationException("Invalid demonstration calculation.");
    var plan = new { targetMeso = target, totalKills = counts.Sum(), partySize = 1, priceDate = today,
        bosses = top.Select((price, i) => new { price.Name, price.Difficulty, crystalMeso = price.Meso, kills = counts[i], totalMeso = price.Meso * counts[i], price.Source }) };
    File.WriteAllText(Path.Combine(source, "demo-5trillion-plan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
}
var replay = new IncomeReplay(clears);
var output = Path.Combine(source, "income-replay-frames"); Directory.CreateDirectory(output);
using var renderer = new IncomeReplayRenderer(replay, new("meso"), demo ? "상위 12종 · 1인 기준 집계 시연" : "저장된 주간 보스 기록", Path.Combine(root, "src", "MapleDay", "Assets"));
const int frameCount = 421;
var timeline = new List<object>();
for (var frame = 0; frame < frameCount; frame++)
{
    var seconds = frame / (double)(frameCount - 1) * IncomeReplayRenderer.Duration;
    var totals = replay.At(Math.Clamp((seconds - .5) / (IncomeReplayRenderer.Duration - 2.5), 0, 1));
    timeline.Add(new { seconds, totals.Total, totals.Count });
    using var image = renderer.Render(seconds, 1920, 1080, stableRanking: true);
    image.Save(Path.Combine(output, $"{frame:000}.png"), ImageFormat.Png);
}
File.WriteAllText(Path.Combine(source, "income-replay-timeline.json"), JsonSerializer.Serialize(timeline));
Console.WriteLine($"{(demo ? "Demo" : "Actual")} weekly income plate: {replay.Total} mesos; {replay.Clears.Count} clears; {frameCount} frames.");
