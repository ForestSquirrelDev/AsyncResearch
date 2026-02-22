using System.Runtime.ExceptionServices;

namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class ThrowExExample
    {
        public static async Task RunThrowExExample()
        {
            await Layer0();
            await Task.Delay(1000);
        }

        private static async Task Layer0()
        {
            await Task.Delay(100);
            await Layer1();
        }

        private static async Task Layer1()
        {
            await Task.Delay(100);
            try
            {
                await Layer2();
            }
            catch (Exception ex)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }
        
        private static async Task Layer2()
        {
            await Task.Delay(100);
            await Layer3();
        }

        private static async Task Layer3()
        {
            await Task.Delay(100);
            await Layer4();
        }
        
        private static async Task Layer4()
        {
            await Task.Delay(100);
            throw new Exception("HORY SHIET!");
        }
    }
}