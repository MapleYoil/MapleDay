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
var hunting = args.Contains("--hunting");
var demo = demoIndex >= 0;
if (demo)
{
    var target = long.Parse(args[demoIndex + 1], System.Globalization.CultureInfo.InvariantCulture);
    var today = new DateOnly(2026, 10, 8);
    var top = ManualWeeklyHistory.Choices.Select(price => CrystalPrices.Find(price.Name, price.Difficulty, today)!)
        .GroupBy(price => SchedulerBossHistory.BossKey(price.Name))
        .Select(group => group.MaxBy(price => price.Meso)!).OrderByDescending(price => price.Meso).Take(12).ToArray();
    var dates = ManualWeeklyHistory.Dates(SchedulerBossHistory.FirstDate, today, today).TakeLast(2).ToArray();
    using var market = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "market-prices.json")));
    var pricedItems = market.RootElement.GetProperty("prices").EnumerateArray()
        .Where(price => price.GetProperty("region").GetString() == "normal" && price.GetProperty("priceEok").GetInt64() > 0)
        .GroupBy(price => price.GetProperty("itemId").GetString()!, StringComparer.Ordinal)
        .Select(group => group.OrderBy(price => price.GetProperty("priceEok").GetInt64()).First())
        .ToDictionary(price => price.GetProperty("itemId").GetString()!, price => new {
            Meso = checked(price.GetProperty("priceEok").GetInt64() * 100_000_000), Date = price.GetProperty("date").GetString(), Variant = price.GetProperty("variant").GetString()
        }, StringComparer.Ordinal);
    if (pricedItems.Count == 0) throw new InvalidOperationException("The demo requires a verified market snapshot with positive prices.");
    var events = dates.SelectMany(date => top.Select(boss => new {
        Date = date, Boss = boss,
        Items = BossLootCatalog.ForBoss(boss.Name, boss.Difficulty).Where(item => pricedItems.ContainsKey(item.Id)).ToArray()
    })).OrderBy(entry => entry.Date).ThenBy(entry => entry.Boss.Name, StringComparer.Ordinal).ToArray();
    var rewards = events.SelectMany(entry => entry.Items).DistinctBy(item => item.Id)
        .OrderBy(item => pricedItems[item.Id].Meso).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
    var drops = events.Select(_ => new List<ReplayLoot>()).ToArray();
    // One genuine-priced item of every eligible kind. The goal never changes an item's price.
    foreach (var item in rewards)
    {
        var eligible = Enumerable.Range(0, events.Length).Where(i => events[i].Items.Any(candidate => candidate.Id == item.Id)).ToArray();
        var index = eligible.OrderBy(i => drops[i].Count).ThenByDescending(i => i).First();
        drops[index].Add(new ReplayLoot(item.Name, pricedItems[item.Id].Meso, item.Icon));
    }
    clears = events.Select((entry, i) => new ReplayClear(entry.Date, entry.Boss.Name,
        entry.Boss.Meso + drops[i].Sum(item => item.Meso), drops[i].ToArray())).ToArray();
    var crystalIncome = events.Sum(entry => entry.Boss.Meso);
    var actualTotal = clears.Sum(clear => clear.Meso);
    if (clears.Select(clear => clear.Boss).Distinct().Count() != 12 || rewards.Length == 0)
        throw new InvalidOperationException("Invalid demonstration calculation.");
    var plan = new { requestedMeso = target, totalMeso = actualTotal, totalKills = clears.Length, exampleClears = true,
        crystalIncome, partySize = 1, region = "normal",
        marketCheckedAt = market.RootElement.GetProperty("fetchedAt").GetString(),
        unitPrices = rewards.Select(item => new { item.Id, item.Name, unitMeso = pricedItems[item.Id].Meso, date = pricedItems[item.Id].Date, variant = pricedItems[item.Id].Variant }),
        settlements = clears.Select(clear => new { clear.Date, clear.Boss, clear.Meso, clear.Loot }) };
    File.WriteAllText(Path.Combine(source, "demo-income-plan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
}
var scope = demo ? "일반 월드 시세 기준 · 처치 예시" : "저장된 주간 보스 기록";
if (hunting)
{
    using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "fragment-prices.json")));
    var market = snapshot.RootElement.EnumerateArray().First(row => row.GetProperty("region").GetString() == "normal");
    var price = market.GetProperty("priceMillion").GetInt64() * 1000000;
    var meso = HuntingIncome.FromLimit(300, 100, 280);
    var rows = Enumerable.Range(0, 200).Select(i => new HuntingIncomeRecord(i.ToString(), "demo", new DateOnly(2026, 10, 9).AddDays(i - 199),
        meso, 150, price, 280, 100, 300)).ToArray();
    clears = rows.Select(row => new ReplayClear(row.Date, "1,000시간 사냥", row.Total,
        [new ReplayLoot("솔 에르다 조각", row.Fragments * row.FragmentUnitPrice, "Hunting/fragment.png", row.Fragments)])).ToArray();
    scope = "Lv.300 · 메소 획득 +280% · 5시간/일";
    File.WriteAllText(Path.Combine(source, "demo-hunting-plan.json"), JsonSerializer.Serialize(new {
        hours = 1000, days = 200, hoursPerLimit = 5, level = 300, bonusPercent = 280,
        fragmentsPerHalfHour = 15, fragments = 30000, unitMeso = price, priceDate = market.GetProperty("date").GetString(),
        mesoIncome = meso * 200, fragmentIncome = price * 30000, totalMeso = rows.Sum(row => row.Total)
    }, new JsonSerializerOptions { WriteIndented = true }));
}
var replay = new IncomeReplay(clears) { Category = hunting ? "hunting" : "boss" };
var output = Path.Combine(source, hunting ? "hunting-replay-frames" : "income-replay-frames"); Directory.CreateDirectory(output);
using var renderer = new IncomeReplayRenderer(replay, new("meso"), scope, Path.Combine(root, "src", "MapleDay", "Assets"));
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
File.WriteAllText(Path.Combine(source, hunting ? "hunting-replay-timeline.json" : "income-replay-timeline.json"), JsonSerializer.Serialize(timeline));
Console.WriteLine($"{(demo ? "Demo" : "Actual")} weekly income plate: {replay.Total} mesos; {replay.Clears.Count} clears; {frameCount} frames.");
