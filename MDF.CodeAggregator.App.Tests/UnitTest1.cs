

using System.IO;
using System.Reflection;

namespace MDF.CodeAggregator.App.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            // D:\Projets\C2S\MDF\MDF.CodeAggregator\MDF.CodeAggregator.App.Tests\bin
            Program.Main(new string[] { 
                "..\\..\\..\\..\\..\\MDF\\mdf.sln",
                "App",
                "Problem1",
                "..\\..\\..\\..\\..\\MDF\\Output"
            });


        }


        [Fact]
        public void Test2()
        {
            // D:\Projets\C2S\IsogradCSharpCodeAggregator\MDF.CodeAggregator.App.Tests\bin

            //D:\Projets\C2S\MDF\MDF
            Program.Main(new string[] {
                "..\\..\\..\\..\\..\\MDF\\MDF\\mdf.sln",
                "App",
                "Problem1",
                "..\\..\\..\\..\\..\\MDF\\MDF\\Output"
            });


        }
    }
}