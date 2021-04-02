using System;

namespace DNSUpdater.Console
{
    class Program
    {
        static void Main(string input, string password)
        {
            System.Console.WriteLine("Testing!");
            var result = Helper.Helper.CreateHash($"{input}:{password}");

            System.Console.WriteLine(result);
            System.Console.ReadLine();
        }
    }
}
