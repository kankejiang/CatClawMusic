using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TagLib;

string path = @"C:\Users\lvjin\Downloads\一半一半 - 希林娜依高&欧阳娣娣 (Didi Ouyang)&王晓赟子&谢可寅&袁一琦.flac";

using var file = TagLib.File.Create(path);
var lyrics = file.Tag.Lyrics;
if (string.IsNullOrWhiteSpace(lyrics))
{
    Console.WriteLine("No embedded lyrics");
    return;
}

var xml = XElement.Parse(lyrics);
XNamespace ttm = "http://www.w3.org/ns/ttml#metadata";
XNamespace ttmlNs = "http://www.w3.org/ns/ttml";

Console.WriteLine("=== Agents ===");
var agents = xml.Descendants(ttm + "agent").ToList();
foreach (var agent in agents)
{
    var id = agent.Attribute(XNamespace.Xml + "id")?.Value ?? agent.Attribute("id")?.Value ?? "?";
    var type = agent.Attribute("type")?.Value ?? "";
    var names = agent.Elements(ttm + "name")
                     .Select(n => n.Value)
                     .Where(v => !string.IsNullOrWhiteSpace(v));
    Console.WriteLine($"  {id} ({type}): {string.Join(", ", names)}");
}

Console.WriteLine("\n=== Body agent usage ===");
var paragraphs = xml.Descendants(ttmlNs + "p").ToList();
var agentCounts = paragraphs
    .GroupBy(p => p.Attribute(ttm + "agent")?.Value ?? "(none)")
    .OrderByDescending(g => g.Count());
foreach (var g in agentCounts)
{
    Console.WriteLine($"  {g.Key}: {g.Count()} lines");
}

Console.WriteLine("\n=== First 20 non-v1 lines ===");
var nonV1 = paragraphs
    .Where(p => (p.Attribute(ttm + "agent")?.Value ?? "v1") != "v1")
    .Take(20)
    .ToList();
foreach (var p in nonV1)
{
    var agent = p.Attribute(ttm + "agent")?.Value ?? "?";
    var begin = p.Attribute("begin")?.Value ?? "?";
    var text = string.Concat(p.Descendants(ttmlNs + "span").Select(s => s.Value)).Trim();
    if (string.IsNullOrEmpty(text)) text = p.Value.Trim();
    Console.WriteLine($"  [{agent} @ {begin}] {text.Substring(0, Math.Min(60, text.Length))}");
}
