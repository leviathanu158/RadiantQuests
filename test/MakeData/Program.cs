// Radiant Quests data file maker. GPLv3 or later.
// Usage: MakeData <Kenshi folder> <out .mod path>
// Writes the mod's Kenshi data file with its own records, made from base-game ones so they look and behave like the game's:
//   - "Guild Parcel" (RadiantQuests.Items.ParcelRecord): a sealed parcel courier quests hand out. Its look (model, icon, size) is the base
//     game's Damaged Book; no trade value (shops pay nothing for it), one per cell. It holds nothing: with the mod removed, a parcel left in a
//     save is just an unknown item the game drops.
using Meitou.Data.Fcs;

if (args.Length < 2) { Console.WriteLine("usage: MakeData <Kenshi folder> <out .mod path>"); return 1; }
string rebirth = Path.Combine(args[0], "data", "rebirth.mod");
var baseFile = FcsReader.ReadFile(rebirth);
const string lookOf = "43927-rebirth.mod";   // Damaged Book
var source = baseFile.Records.FirstOrDefault(r => r.StringId == lookOf);
if (source is null) { Console.WriteLine($"{lookOf} not found in {rebirth}"); return 1; }

var parcel = new FcsRecord
{
    Type = source.Type,
    Id = 1,
    Name = "Guild Parcel",
    StringId = "1-RadiantQuests.mod",
    Flags = source.Flags,
};
foreach (var (k, v) in source.Bools) parcel.Bools[k] = v;
foreach (var (k, v) in source.Floats) parcel.Floats[k] = v;
foreach (var (k, v) in source.Ints) parcel.Ints[k] = v;
foreach (var (k, v) in source.Vector3S) parcel.Vector3S[k] = v;
foreach (var (k, v) in source.Vector4S) parcel.Vector4S[k] = v;
foreach (var (k, v) in source.Strings) parcel.Strings[k] = v;
foreach (var (k, v) in source.Filenames) parcel.Filenames[k] = v;
// Its own facts: no worth to anyone but the guild, one per cell, a little heavier than a book.
parcel.Ints["value"] = 0;
parcel.Ints["stackable"] = 1;
parcel.Ints["item function"] = 0;
parcel.Bools["trade item"] = false;
parcel.Floats["weight kg"] = 2;
parcel.Strings["description"] = "A sealed parcel of the Adventurers' Guild, to be handed to the guild clerk named on the quest. "
    + "Worthless to anyone else, and the guild knows who carries it.";

var file = new FcsFile
{
    FileType = FcsFileType.V16,
    Version = 1,
    Author = "RadiantQuests",
    Description = "Radiant Quests for Meitou (code mod): repeatable guild quests, rep and ranks. Holds the Guild Parcel item for courier quests.",
    NextId = 2,
};
file.Records.Add(parcel);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
FcsWriter.WriteFile(file, args[1]);
// Read it back: the file must load as the game would.
var check = FcsReader.ReadFile(args[1]);
Console.WriteLine($"wrote {args[1]}: {check.Records.Count} record(s): " + string.Join(", ", check.Records.Select(r => $"{r.Name} ({r.StringId}, type {r.Type})")));
return 0;
