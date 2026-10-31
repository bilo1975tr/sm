namespace StreamMesh.Core
{
    public static class AppConfig
    {
        public const string GitHubRepoUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/main";
        public const string FirebaseDatabaseUrl = "https://streammesh-p2p-default-rtdb.europe-west1.firebasedatabase.app";

        public static string GetGitHubLanguageUrl(string safeLang)
        {
            return $"{GitHubRepoUrl}/channels_{safeLang}.json";
        }
    }
}
