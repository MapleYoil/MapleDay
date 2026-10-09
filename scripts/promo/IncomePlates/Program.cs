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
    var dates = ManualWeeklyHistory.Dates(SchedulerBossHistory.FirstDate, today, today).TakeLast(2).ToArray();
    // Example loot settlements, not a claim about market prices or drop probabilities.
    // Every eligible reward from a boss can appear in the same clear; no one-item limit.
    var events = dates.SelectMany((date, week) => top.Select((boss, rank) => new {
        Date = date, Boss = boss, Weight = (decimal)(1 + week * week) * (top.Length - rank),
        Items = BossLootCatalog.ForBoss(boss.Name, boss.Difficulty)
    })).Where(entry => entry.Items.Count > 0).OrderBy(entry => entry.Date).ThenBy(entry => entry.Boss.Name, StringComparer.Ordinal).ToArray();
    var crystalIncome = events.Sum(entry => entry.Boss.Meso);
    var lootTarget = target - crystalIncome;
    if (lootTarget <= 0) throw new InvalidOperationException("Demonstration total must exceed the solo crystal income.");
    var weightTotal = events.Sum(entry => entry.Weight);
    var allocated = 0L;
    clears = events.Select((entry, index) => {
        var budget = index == events.Length - 1 ? lootTarget - allocated : (long)decimal.Floor(lootTarget * entry.Weight / weightTotal);
        allocated += budget;
        var rewardWeight = entry.Items.Select((_, item) => entry.Items.Count - item).Sum();
        var paid = 0L;
        var drops = entry.Items.Select((item, number) => {
            var amount = number == entry.Items.Count - 1 ? budget - paid
                : (long)decimal.Floor(budget * (decimal)(entry.Items.Count - number) / rewardWeight);
            paid += amount;
            return new ReplayLoot(item.Name, amount, item.Icon);
        }).ToArray();
        return new ReplayClear(entry.Date, entry.Boss.Name, entry.Boss.Meso + budget, drops);
    }).ToArray();
    if (clears.Sum(clear => clear.Meso) != target || clears.Select(clear => clear.Boss).Distinct().Count() != 12)
        throw new InvalidOperationException("Invalid demonstration calculation.");
    var plan = new { targetMeso = target, totalKills = clears.Length, examplePrices = true, crystalIncome, partySize = 1,
        settlements = clears.Select(clear => new { clear.Date, clear.Boss, clear.Meso, clear.Loot }) };
    File.WriteAllText(Path.Combine(source, "demo-income-plan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
}
var replay = new IncomeReplay(clears);
var output = Path.Combine(source, "income-replay-frames"); Directory.CreateDirectory(output);
using var renderer = new IncomeReplayRenderer(replay, new("meso"), demo ? "상위 12종 결정 + 물욕템 · 예시" : "저장된 주간 보스 기록", Path.Combine(root, "src", "MapleDay", "Assets"));
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
