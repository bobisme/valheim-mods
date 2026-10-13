// Stand-ins for other mods' published Api classes, for the ApiBinder tests.
namespace Dummy
{
    public static class NewApi
    {
        public const int Version = 2;
        public static int Twice(int x) => x * 2;
        public static string Hello(string who) => "hi " + who;
    }
    public static class OldApi // (published before Version existed: version 0)
    {
        public static bool Ping() => true;
    }
}
