using System.Collections.Generic;
using System.Threading.Tasks;

namespace LRS.TestTools.LoadSim
{
    public abstract class ConsoleApplication
    {
        public abstract Task<int> RunAsync(Dictionary<string, string> args);

        public async Task<int> ExecuteAsync(string[] args)
        {
            var arguments = ParseArgs(args);
            return await RunAsync(arguments);
        }

        private Dictionary<string, string> ParseArgs(string[] args)
        {
            var argsDict = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 < args.Length)
                {
                    argsDict[args[i].TrimStart('-')] = args[i + 1];
                }
            }
            return argsDict;
        }
    }
}