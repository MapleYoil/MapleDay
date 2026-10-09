using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using WzComparerR2.WzLib;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var fragmentOnly = args.Contains("--fragment");
var game = args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? @"C:\Nexon\Maple\Data";
var targets = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText("scripts/LootAssetExtractor/rewards.json"))!;
string Normal(string text) => string.Concat(text.Replace(" 4레벨", "").Where(character => !char.IsWhiteSpace(character)));
string DisplayName(string name) => name is "컨티뉴어스 링" or "리스트레인트 링" ? name + " 4레벨" : name;
if (fragmentOnly) targets = new Dictionary<string, string[]> { ["사냥"] = ["솔 에르다 조각"] };
var names = targets.Values.SelectMany(names => names).Select(Normal).ToHashSet();
var folders = new Dictionary<string, (Wz_Structure Structure, Wz_Node Root)>();
Wz_Node Folder(string folder)
{
    if (!folders.TryGetValue(folder, out var cached))
    {
        var structure = new Wz_Structure { TextEncoding = Encoding.GetEncoding(949) };
        Wz_Node root = null!;
        structure.LoadWzFolder(Path.Combine(game, folder.Replace('/', '\\')), ref root);
        folders[folder] = cached = (structure, root);
    }
    return cached.Root;
}
Wz_Node Resolve(string path)
{
    var parts = path.Split('/'); var index = Array.FindIndex(parts, part => part.EndsWith(".img"));
    var root = Folder(string.Join('/', parts.Take(index)));
    var image = root.FindNodeByPath(true, parts[index])!.GetValue<Wz_Image>();
    if (!image.TryExtract(out var error)) throw new InvalidOperationException(path, error);
    var node = image.Node.FindNodeByPath(true, parts[(index + 1)..])!;
    for (var depth = 0; depth < 10; depth++)
    {
        if (node.Value is Wz_Uol uol) { node = uol.HandleUol(node); continue; }
        if (node.FindNodeByPath("_outlink")?.Value is string outside) return Resolve(outside);
        if (node.FindNodeByPath("_inlink")?.Value is string inside) { node = image.Node.FindNodeByPath(true, inside.Split('/'))!; continue; }
        return node;
    }
    throw new InvalidOperationException("Cyclic resource link: " + path);
}
var found = new List<(string Id, string Name, string Path)>();
void Scan(Wz_Node node, string category, string section)
{
    if (node.FindNodeByPath("name")?.Value is string name && names.Contains(Normal(name)) && int.TryParse(node.Text, out var id))
    {
        var path = category == "Eqp" ? $"Character/{section}/{id:D8}.img/info/icon"
            : $"Item/{category}/{id / 10000:D4}.img/{id:D8}/info/icon";
        found.Add((id.ToString(), name, path));
    }
    foreach (var child in node.Nodes) Scan(child, category, category == "Eqp" && child.Text != "Eqp" && !int.TryParse(child.Text, out _) ? child.Text : section);
}
try
{
    foreach (var category in new[] { "Eqp", "Consume", "Etc", "Install" })
    {
        var image = Folder("String").FindNodeByPath(true, (category == "Install" ? "Ins" : category) + ".img")!.GetValue<Wz_Image>();
        if (!image.TryExtract(out var error)) throw new InvalidOperationException(category, error);
        Scan(image.Node, category, "");
    }
    var destination = fragmentOnly ? "src/MapleDay/Assets/Hunting" : "src/MapleDay/Assets/BossLoot"; Directory.CreateDirectory(destination);
    var items = new List<object>(); var manifest = new List<object>();
    foreach (var item in found.GroupBy(item => item.Name).Select(group => group.First()))
    {
        var node = Resolve(item.Path);
        using var bitmap = node.GetValue<Wz_Png>().ExtractPng();
        var icon = fragmentOnly ? "Hunting/fragment.png" : "BossLoot/" + item.Id + ".png";
        var file = "src/MapleDay/Assets/" + icon;
        bitmap.Save(file, ImageFormat.Png);
        items.Add(new { item.Id, Name = DisplayName(item.Name), Bosses = targets.Where(pair => pair.Value.Any(name => Normal(name) == Normal(item.Name))).Select(pair => pair.Key).ToArray(), Icon = icon });
        manifest.Add(new { item.Id, Name = DisplayName(item.Name), sourceName = item.Name, source = item.Path, width = bitmap.Width, height = bitmap.Height,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant() });
        Console.WriteLine(item.Name);
    }
    var missing = names.Except(found.Select(item => Normal(item.Name))).ToArray();
    if (missing.Length > 0) Console.WriteLine("MISSING: " + string.Join(" / ", missing));
    if (!fragmentOnly) File.WriteAllText("src/MapleDay.Core/BossLootItems.json", JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(destination + "/manifest.json", JsonSerializer.Serialize(new { guide = "https://maplestory.nexon.com/guide/n23gameinformation/articles/459", soulGuide = "https://maplestory.nexon.com/guide/n23gameinformation/articles/416", ringGuide = "https://maplestory.nexon.com/Guide/OtherProbability/bossRingBox/ringBoxRedJade", resources = manifest }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Extracted {items.Count} unique reward icons.");
}
finally { foreach (var folder in folders.Values) folder.Structure.Clear(); }
