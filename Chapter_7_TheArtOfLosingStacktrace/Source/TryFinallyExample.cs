namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class TryFinallyExample
    {
        public static void RunTryFinallyExample()
        {
            try
            {
                throw new Exception("HORY SHIET!");
            }
            catch
            {
            }
            finally
            {
                throw new Exception("Oh no!");
            }
        }
    }
}