using System.Threading.Tasks;

namespace MDF.CodeAggregator.App.Tests
{
    public class UnitTest1
    {
        [Fact]
        public async Task Test2()
        {
            // D:\Projets\C2S\IsogradCSharpCodeAggregator\MDF.CodeAggregator.App.Tests\bin

            //D:\Projets\C2S\MDF\MDF
            await Program.Main(new string[] {
                "..\\..\\..\\..\\..\\MDF\\MDF\\mdf.sln",
                "App._2024.Jo.Problem1",
                "..\\..\\..\\..\\..\\MDF\\MDF\\Output"
            });


        }
    }
}