namespace AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace
{
    public static class AsyncVoidNestedException
    {
        public static async Task AsyncVoidExceptionTest()
        {
            _ = Layer0();
            await Task.Delay(3000);
        }

        private static async Task Layer0()
        {
            await Task.Delay(100);
            await Layer1();
        }

        private static async Task Layer1()
        {
            await Task.Delay(100);
            await Layer2();
        }
        
        private static async Task Layer2()
        {
            await Task.Delay(100);
            await Layer3();
        }

        private static async Task Layer3()
        {
            await Task.Delay(100);
            Layer4();
        }
        
        private static async void Layer4()
        {
            await Task.Delay(100);
            throw new Exception("HORY SHIET!");
        }
    }
}