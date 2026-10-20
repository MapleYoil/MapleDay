using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using WzComparerR2.WzLib;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var structure = new Wz_Structure { TextEncoding = Encoding.GetEncoding(949) };
Wz_Node root = null!;
try
{
    structure.LoadWzFolder(args.FirstOrDefault() ?? @"C:\Nexon\Maple\Data\UI", ref root);
    if (args.Contains("--names"))
    { foreach (var node in root.Nodes.Where(node => new[] { "Equip", "Deco", "Symbol", "Item", "Basic", "Avatar", "Character", "Custom" }.Any(word => node.Text.Contains(word, StringComparison.OrdinalIgnoreCase)))) Console.WriteLine(node.Text); return; }
    if (args.Contains("--inventory"))
    {
        foreach (var candidate in root.Nodes.Where(node => node.Text.Contains("Window") || node.Text.Contains("Equip")))
        {
            Console.WriteLine(candidate.Text);
            if (candidate.Value is Wz_Image image && image.TryExtract(out _))
                foreach (var child in image.Node.Nodes.Where(node => node.Text.Contains("Equip", StringComparison.OrdinalIgnoreCase))) Dump(child, 0, 3);
        }
        return;
    }
    var imageName = args.ElementAtOrDefault(1) ?? "UIWindow2.img";
    var imageRoot = root.FindNodeByPath(imageName)!.GetValue<Wz_Image>();
    if (!imageRoot.TryExtract(out var error)) throw new InvalidOperationException(imageName, error);
    var path = args.ElementAtOrDefault(2) ?? "Equip";
    var target = path == "." ? imageRoot.Node : imageRoot.Node.FindNodeByPath(true, path.Split('/')) ?? throw new ArgumentException("Missing node " + path);
    if (!args.Contains("--export")) { Dump(target, 0, 5); return; }
    var folder = "src/MapleDay/Assets/Equipment/" + imageName.Replace(".img", "") + (path == "." ? "" : "_" + path.Replace('/', '_')); Directory.CreateDirectory(folder);
    var entries = new List<object>();
    var keep = new HashSet<string>(["backgrnd", "backgrnd2", "tabbar", "Equip/backgrnd", "Tab/enabled/0", "Tab/disabled/1", "Tab/disabled/2", "Tab/disabled/3", "Tab/disabled/4"]);
    void Export(Wz_Node node, string relative)
    {
        if (node.Value is Wz_Png)
        {
            if (imageName == "UIEquip.img" && !args.Contains("--all"))
            {
                var full = path == "." ? relative : path + "/" + relative;
                var interactionState = new[] { "Equip/EquipTab/button:symbol/", "Equip/EquipTab/button:DecoUI/", "Deco/CoordiTab/button:EquipUI/", "Deco/CoordiTab/button:CoordiPreset/" }.Any(full.StartsWith);
                if (!new[] { "Equip/main/", "Equip/EquipTab/", "Equip/AvatarImage/canvas:", "Equip/Symbol/", "Deco/main/", "Deco/CoordiTab/", "Deco/CoordiPreset/" }.Any(full.StartsWith)
                    || new[] { "effect", "Ani/", "titleSkin", "skillRing", "presetSelected", "button:close", "button:presetApplication" }.Any(full.Contains)
                    || (!interactionState && new[] { "mouseOver", "pressed", "disabled" }.Any(full.Contains))) return;
            }
            if (imageName == "UIWindow4.img" && !keep.Contains(relative) && !relative.StartsWith("Equip/Slots/", StringComparison.Ordinal)) return;
            if (imageName == "UIToolTip.img" && path == "Item/Equip" && !args.Contains("--all")
                && relative != "imgFont/atkPow/equipped"
                && !new[] { "frame/common/", "itemIcon/", "textIcon/starForce/", "textIcon/potential/title/", "textIcon/additionalPotential/title/" }.Any(relative.StartsWith)) return;
            var canvas = Resolve(node, imageRoot.Node);
            using var bitmap = canvas.GetValue<Wz_Png>().ExtractPng();
            var name = relative.Replace('/', '_').Replace(':', '_').Replace("+", "plus").Replace("%", "percent").Replace(".", "dot") + ".png";
            bitmap.Save(Path.Combine(folder, name), ImageFormat.Png);
            var origin = node.FindNodeByPath("origin")?.Value as Wz_Vector;
            entries.Add(new { file = name, source = Path.GetFileName((args.FirstOrDefault() ?? "UI").TrimEnd('\\', '/')) + "/" + imageName + "/" + path + "/" + relative,
                width = bitmap.Width, height = bitmap.Height, originX = origin?.X ?? 0, originY = origin?.Y ?? 0,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, name)))).ToLowerInvariant() });
            Console.WriteLine(name + $" {bitmap.Width}x{bitmap.Height} origin {origin?.X},{origin?.Y}"); return;
        }
        foreach (var child in node.Nodes) Export(child, relative.Length == 0 ? child.Text : relative + "/" + child.Text);
    }
    Export(target, "");
    File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    if (imageName == "UIToolTip.img" && path == "Item/Equip")
    {
        var fonts = new Dictionary<string, object>();
        void ExportFonts(Wz_Node node, string relative)
        {
            if (node.Nodes["font"]?.Value is string family)
                fonts[relative] = new
                {
                    source = "UI/UIToolTip.img/Item/Equip/font/" + relative,
                    family, size = node.Nodes["fontSize"].GetValue<int>(), bold = node.Nodes["fontBold"].GetValue<int>(),
                    color = node.Nodes["fontColor"].Value.ToString()
                };
            else foreach (var child in node.Nodes) ExportFonts(child, relative.Length == 0 ? child.Text : relative + "/" + child.Text);
        }
        ExportFonts(target.Nodes["font"], "");
        File.WriteAllText(Path.Combine(folder, "fonts.json"), JsonSerializer.Serialize(fonts, new JsonSerializerOptions { WriteIndented = true }));
    }
}
finally { structure.Clear(); }

Wz_Node Resolve(Wz_Node node, Wz_Node image)
{
    for (var depth = 0; depth < 20; depth++)
    {
        if (node.Value is Wz_Uol uol) { node = uol.HandleUol(node); continue; }
        if (node.FindNodeByPath("_inlink")?.Value is string inside) { node = image.FindNodeByPath(true, inside.Split('/'))!; continue; }
        if (node.FindNodeByPath("_outlink")?.Value is string outside)
        {
            var parts = outside.Split('/'); var start = Array.FindIndex(parts, part => part.EndsWith(".img"));
            var current = root;
            foreach (var part in parts.Skip(1).Take(start - 1)) current = current.Nodes[part];
            var linked = current.Nodes[parts[start]].GetValue<Wz_Image>();
            if (!linked.TryExtract(out var error)) throw new InvalidOperationException(outside, error);
            image = linked.Node; node = image.FindNodeByPath(true, parts[(start+1)..])!; continue;
        }
        return node;
    }
    throw new InvalidOperationException("Cyclic equipment UI link");
}
void Dump(Wz_Node node, int depth, int max)
{
    var data = node.Value is Wz_Png png ? $"PNG {png.Width}x{png.Height}" : node.Value is Wz_Vector v ? $"({v.X},{v.Y})" : node.Value?.ToString();
    Console.WriteLine(new string(' ', depth*2) + node.Text + " " + data);
    if (depth < max) foreach (var child in node.Nodes) Dump(child, depth+1, max);
}
