using gbrueckl.Azure.DataFactory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LocalTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string path = @"..\\..\\..\\\DataFactory\DataFactory.dfproj";
            ADFLocalEnvironment env = new ADFLocalEnvironment(path);
            env.ExecuteActivity(
                "CopyBlobToDocDB",
                "PerformUpsert",
                new DateTime(2017, 1, 1),
                new DateTime(2017, 2, 2));
            Console.ReadLine();
        }
    }
}
