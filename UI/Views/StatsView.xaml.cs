using System.Windows.Controls;
using StreamMesh.Core.Database;

namespace StreamMesh.UI.Views
{
    public partial class StatsView : System.Windows.Controls.UserControl
    {
        private readonly DatabaseEngine _db = new DatabaseEngine();

        public StatsView()
        {
            InitializeComponent();
            LoadStats();
        }

        private async void LoadStats()
        {
            int count = await _db.GetTotalChannelCountAsync();
            TotalChannelsText.Text = count.ToString();
        }
    }
}
