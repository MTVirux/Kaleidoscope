namespace Kaleidoscope.Gui.Widgets.Common;

/// <summary>
/// A shared pool of flavour-text messages for loading screens,
/// empty states, or anywhere a rotating quip is needed.
/// </summary>
public static class FlavourText
{
    /// <summary>
    /// Returns a random message from the built-in pool.
    /// </summary>
    public static string GetRandom(Random random)
        => Messages[random.Next(Messages.Length)];

    /// <summary>
    /// Returns a random message from the combined pool of built-in and
    /// additional project-specific messages.
    /// </summary>
    public static string GetRandom(Random random, ReadOnlySpan<string> additional)
    {
        var total = Messages.Length + additional.Length;
        var index = random.Next(total);
        return index < Messages.Length ? Messages[index] : additional[index - Messages.Length];
    }

    /// <summary>
    /// The full set of built-in messages, exposed for enumeration or custom selection logic.
    /// </summary>
    public static ReadOnlySpan<string> All => Messages;

    /// <summary>
    /// The number of built-in messages.
    /// </summary>
    public static int Count => Messages.Length;

    private static readonly string[] Messages =
    [
        // ── FFXIV gameplay ──────────────────────────────────────────
        "Polishing the kaleidoscope",
        "Consulting the marketboard",
        "Counting your gil",
        "Herding retainers",
        "Bribing the Moogle postman",
        "Recalibrating the aetherometer",
        "Negotiating with Rowena",
        "Dusting off the ledger",
        "Feeding the chocobo",
        "Gathering crystals",
        "Warming up the database",
        "Syphoning the aether",
        "Summoning services",
        "Stacking inventory tetris",
        "Rolling for loot",
        "Checking the retainer bell",
        "Tuning the orchestrion",
        "Praying to the Terrors",
        "Rebuilding Garlemald",
        "Rebuilding Ishgard",
        "Rebuilding Doma",
        "RPing in Limsa",
        "Filling out sightseeing log",
        "Waiting for Timed Nodes",
        "Waiting for cooldowns",
        "Waiting for DPS queues",
        "Drinking with Godbert",
        "Building Triple Triad decks",
        "Trying to learn Majhong rules",
        "Learning Majhong rules (again)",
        "Glamouring retainers",
        "Casting Ultima",
        "Looking for LB Button",
        "Returning to the Waking Sands",
        "Losing housing lottery",
        "Playing mini-cactpot",
        "Drifting Chocobos",
        "Petting Fenrir",
        "Looking respectfully",
        "Undercutting the marketboard",
        "Refueling submersibles",
        "Crafting custom deliveries",
        "Looking up rotation guide",
        "Naming retainers",
        "Failing Rapid Synth",
        "Attempting Necromancer title",
        "Farming Pteranodon mount",
        "Waiting for Big Fish",
        "Reading Faloop drama",
        "Spawning S-Rank",
        "Polishing Tonberry Knife",
        "Dancing at the honeybee",
        "Attending Eternal Bond ceremony",
        "Completing BLU log",
        "Doing MSQ",
        "Inspecting Hildibrand",
        "Fighting god",
        "Doing sidequests",
        "Giving out commendations",
        "Dying in floor 99",
        "1v9-ing in CC",
        "Visiting the ISS",
        "Petting Alpha",
        "Tuning Omega",
        "Freeing the dragons",
        "Abandoning Praetorium",
        "Abandoning Castrum Meridianum",
        "Guaranteeing LotA in roulette",
        "Reporting RMT bots",
        "Waiting for maintenance to end",
        "Waiting in Party Finder",
        "Griefing the Party Finder", // Atrina
        "Parsing grey", // Atrina
        "\"Intentionally\" sandbagging",
        "*Ascian Finger Snap*",
        "Skipping: A long fall (Pulse)",

        // ── FFXIV lore ──────────────────────────────────────────────
        "Attending the Convocation",
        "Communing with Hydaelyn",
        "Such devastation",
        "Rescuing Y'shtola (again)",
        "Blaming Ascians",
        "This is Thancred",
        "Reading lore",

        // ── FFXIV races ─────────────────────────────────────────────
        "Petting Miqo'te",
        "Petting Lalafell",
        "Sunbathing Au'Ra",
        "Brushing off Viera",

        // ── Pop-culture / meta ──────────────────────────────────────
        "Exorcising Xenos",
        "Loading Jojo references",
        "Raphael take the wheel",
        "Preparing big fat tacos",
        "Brewing some coffee",
        "Smoking a cigarette",
        "90002: Connection to server was lost", // Glarator

        // ── Finance / accounting parody ─────────────────────────────
        "Auditing retainer profits",
        "Calculating cost basis",
        "Diversifying investments",
        "Filing Ul'dah tax returns",
        "Forecasting materia futures",
        "Hedging against patch day crashes",
        "Laundering gil through housing",
        "Liquidating surplus inventory",
        "Maximizing ROI on ventures",
        "Monitoring price volatility",
        "Optimizing supply chains",
        "Projecting quarterly earnings",
        "Rebalancing portfolio",
        "Reviewing profit margins",
        "Shorting Allagan Tomestones",
        "Tracking inflation rates",
        "Writing off glamour expenses",
        "Embezzling from the FC chest",
        "Adjusting for inflation (SFW)",
        "Compounding interest on gil",
        "Insider trading rare dyes",
        "Cooking the books at Rowena's",
        "Issuing Ishgardian bonds",
        "Penny-pinching on repairs",
        "Running a Ponzi venture scheme",
        "Expensing reports to Tataru",
        "Taxing Limsa market stalls",
        "Valuing intangible glamour assets",
        "Yield farming with botanists",
        "Evading the Brass Blades audit",
        "Factoring in retainer overhead",
        "Going public on Sapphire Ave",
        "Negotiating venture capital",
        "Off-shoring gil to Kugane",
        "Price-fixing with the syndicate",
        "Raising Ul'dah debt ceiling",
        "Amortizing relic weapon costs",
        "Billing the Scions for expenses",
        "Closing quarter in Revenant's",
        "Day-trading crafting mats",
        "Appraising dungeon drop value",
        "Gilding the balance sheet",
        "Hoarding during patch speculation",
        "Claiming glamour tax deductions",
        "Marking up vendor trash",
        "Overstating assets to Tataru",
        "Penny-stocking Doman bonds",
        "Quantifying levelling costs",
        "Restructuring FC debt",
        "Siphoning Jumbo Cactpot gil",
        "Trading futures on patch notes",
        "Underreporting crafting income",
        "Vetting venture risk profiles",
        "Withholding Monetarist gil",
        "Auctioning Omega's parts",
        "Calling margin on materia bets",
        "Expensing teleport fees",
        "Garnishing adventurer wages",
        "Hiring a Lalafell accountant",
        "IPO-ing the Gold Saucer",
        "Kiting cheques across servers",
        "Lobbying the Syndicate",
        "Misappropriating GC seal funds",
        "Notarizing Moogle mail contracts",
        "Overcharging for HQ crafts",
        "Quietly moving funds to FC",
        "Refinancing the FC workshop",
        "Analyzing venture droprates",
        "Taking a loss on last tier's gear",
        "Writing IOUs to beast tribes",
        "Arbitraging cross-world prices",
        "Deducting repair costs",
        "Embargoing Garlean imports",
        "Fudging the P&L for FC meetings",
        "Depreciating minion collections",
        "Forging guild receipts",
        "Ghostwriting Tataru's reports",
        "Incorporating in Crystarium",
        "Keeping two sets of books",
        "Listing losses as donations",
        "Manufacturing materia demand",
        "Netting cross-DC arbitrage",
        "Opening a shell in Thavnair",
        "Pilfering Adventurer in Need gil",
        "Quoting inflated appraisals",
        "Racketeering in the Firmament",
        "Skimming leve rewards",
        "Tax-sheltering retainer gil",
        "Underwriting subaquatic voyages",
        "Vaulting Rowena's rates",
        "Xeroxing fake HQ certificates",
        "Yielding gardening dividends",
        "Zeroing in on the market",
    ];
}
