using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    public enum Greeting { Hello, Thanks, Fear, Weather, Night, Awe, Welcome }

    // What villagers call out in the world: a short line in Old Norse with the English under it. {name} is the player's name;
    // "jarl" is said as is. Only letters the game's font has (þ ð æ ö and the accented vowels; never ǫ or œ).
    public static class Greetings
    {
        public static readonly Dictionary<Greeting, (string norse, string english)[]> Lines = new Dictionary<Greeting, (string, string)[]>
        {
            [Greeting.Hello] = new[]
            {
                ("Heill, jarl!", "Hail, jarl!"),
                ("Heill ok sæll, {name}!", "Hale and happy, {name}!"),
                ("Vel kominn heim!", "Welcome home!"),
                ("Góðan dag, jarl.", "Good day, jarl."),
                ("Gott er at sjá þik.", "Good to see you."),
                ("Ver velkominn, {name}!", "Be welcome, {name}!"),
                ("Heill þú, {name}!", "Hail to you, {name}!"),
                ("Hvat er títt, jarl?", "What news, jarl?"),
                ("Sæll, jarl!", "Well met, jarl!"),
                ("Þar ertu, {name}!", "There you are, {name}!"),
            },
            [Greeting.Thanks] = new[]
            {
                ("Þökk fyrir!", "Thank you!"),
                ("Haf þökk, jarl.", "Have my thanks, jarl."),
                ("Þat var vel gert.", "That was well done."),
                ("Ek man þetta.", "I'll remember this."),
                ("Mikil er gjöf þín.", "A great gift."),
                ("Góðr ertu, {name}.", "You're good to me, {name}."),
                ("Þökk, {name}!", "Thanks, {name}!"),
            },
            [Greeting.Fear] = new[]
            {
                ("Til vápna!", "To arms!"),
                ("Óvinir koma!", "Foes are coming!"),
                ("Hjálpi oss Þórr!", "Thor help us!"),
                ("Verjum hallina!", "Defend the hall!"),
                ("Rísið upp, allir!", "Up, all of you!"),
                ("Óðinn, gæt vár!", "Odin, guard us!"),
                ("Hér koma þeir!", "Here they come!"),
            },
            [Greeting.Weather] = new[]
            {
                ("Nú rignir enn.", "Raining again."),
                ("Illt er veðrit.", "Foul weather."),
                ("Þórr ekr um himin.", "Thor's driving across the sky."),
                ("Kalt ok vátt.", "Cold and wet."),
                ("Inn undir þak!", "In under a roof!"),
                ("Blautr sem selr.", "Wet as a seal."),
                ("Veðrit versnar.", "The weather's turning."),
            },
            [Greeting.Night] = new[]
            {
                ("Góða nótt, jarl.", "Good night, jarl."),
                ("Sof vel, {name}.", "Sleep well, {name}."),
                ("Dagr er liðinn.", "The day is done."),
                ("Hvílumsk nú.", "Let's rest now."),
                ("Góða nótt, {name}.", "Good night, {name}."),
                ("Á morgin vinnum vér meira.", "More work tomorrow."),
            },
            [Greeting.Awe] = new[]
            {
                ("Sjáið himininn!", "Look at the sky!"),
                ("Goðin dansa í nótt.", "The gods dance tonight."),
                ("Valkyrjur ríða.", "The valkyries are riding."),
                ("Bifröst logar.", "Bifrost is burning."),
                ("Aldri sá ek slíkt.", "I've never seen the like."),
                ("Himinninn brennr grænn.", "The sky's burning green."),
                ("Óðinn horfir á oss.", "Odin is watching us."),
                ("Fagrt er þat!", "It's beautiful!"),
            },
            [Greeting.Welcome] = new[]
            {
                ("Vel kominn heim, {name}!", "Welcome home, {name}!"),
                ("Skipit er komit!", "The ship is in!"),
                ("Hvat er títt af hafinu?", "What news from the sea?"),
                ("Heill aftr, jarl!", "Hail, jarl, back again!"),
                ("Vér biðum þín.", "We were waiting for you."),
            },
        };

        // One line for that moment, with the player's name in it (or "jarl" when there's no player).
        // (of an evening, "good day" becomes "good evening")
        public static (string norse, string english) Pick(Greeting kind, Random rng, string name, bool evening = false)
        {
            var all = Lines[kind];
            if (evening) all = all.Select(l => l.norse.StartsWith("Góðan dag") ? ("Gott kveld, jarl.", "Good evening, jarl.") : l).ToArray();
            var (n, e) = all[rng.Next(all.Length)];
            string who = string.IsNullOrEmpty(name) ? "jarl" : name;
            return (n.Replace("{name}", who), e.Replace("{name}", who));
        }
        // As it shows over their head: the Old Norse small and in italics, the English beneath.
        public static string Show((string norse, string english) line) => $"<size=75%><i>{line.norse}</i></size>\n{line.english}";
        public static readonly string Glyphs = "þðæöáéíóúÞÐÆÖÁÉÍÓÚ";
        public static IEnumerable<char> Letters() => Lines.Values.SelectMany(a => a).SelectMany(l => l.norse + l.english).Distinct();
    }
}
