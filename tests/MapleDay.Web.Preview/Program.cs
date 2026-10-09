using MapleDay.Web;

// Synthetic, loopback-only fixture for headless responsive browser QA. Never opens the Windows app or calls NEXON.
await using var host = new WebHostServer();
var root = Directory.GetCurrentDirectory();
var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(9));
var chars = Enumerable.Range(1, 3).Select(index => new WebCharacter("sample" + index, "메이플" + index, "오로라", "캡틴", 291,
    "Lv. 291 (43.810%)", "저장된 PC 상태 · 20:00 조회", DateTimeOffset.UtcNow, 6 + index,
    new[] { new WebEntry("[일일 퀘스트] 세르니움 조사", "", "", "0", " / 100", false, false, "진행 중", null, null, null),
        new WebEntry("몬스터파크", "", "", "7", " / 14", false, false, "미완료", null, null, null) },
    new[] { new WebEntry("무릉도장", "", "", "60", " 층", false, false, "미완료", null, null, null),
        new WebEntry("[길드] 지하 수로", "", "", "29165", " 점", false, false, "미완료", null, null, null),
        new WebEntry("[몬스터파크] 익스트림 몬스터파커에 도전해보겠나?", "", "", "", "", true, false, "완료", null, null, null) },
    new[] { new WebEntry("스우", "extreme", "bossWeekly", "", "", true, false, "완료", 2, "Bosses/icon_13.png", "Difficulty/extreme.png"),
        new WebEntry("루시드", "hard", "bossWeekly", "", "", false, false, "미완료", 2, "Bosses/icon_19.png", "Difficulty/hard.png"),
        new WebEntry("유피테르", "normal", "bossWeekly", "", "", false, true, "Lv. 295", 1, "Bosses/icon_38.png", "Difficulty/normal.png"),
        new WebEntry("검은 마법사", "hard", "bossMonthly", "", "", true, false, "완료", 6, "Bosses/icon_25.png", "Difficulty/hard.png") },
    new(780_000_000, 1_690_000_000, 13_120_000_000,
        new[] { new WebClear(today.ToString("yyyy-MM-dd"), "스우", "extreme", 2, 630_000_000, true) },
        250_000_000, 0, [new(today.ToString("yyyy-MM-dd"), "스우 · 익스트림", "루즈 컨트롤 머신 마크", "BossLoot/1012632.png", 3,
            "3인 · 2:1:1 · 내 순번 2 · 총액 6억", 150_000_000)]),
    new("+10.0조 (3.1%)", "+1.4조 (0.43%)", "약 127일 16시간", 43.81,
        Enumerable.Range(0, 7).Select(day => new WebExpPoint(today.AddDays(day - 7).ToString("yyyy-MM-dd"), 291,
            (10_000_000_000_000L + day * 1_000_000_000_000).ToString(), 35 + day)).ToArray()), null)).ToArray();
host.Publish(new("1.0.43.0", DateTimeOffset.UtcNow, chars, "both", 1500), new Dictionary<string, byte[]>());
await host.StartAsync(17832, WebPassword.Create("preview-mapleday"), Path.Combine(root, "src", "MapleDay", "Assets"), loopbackOnly: true);
Console.WriteLine("Headless QA fixture: " + host.LocalUrl);
await Task.Delay(Timeout.Infinite);
