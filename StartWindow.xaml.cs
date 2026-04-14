using System.Windows;

namespace ZEGAudioEngineAI
{
    public partial class StartWindow : Window
    {
        public StartWindow()
        {
            InitializeComponent();
        }

        private void OpenMixing_Click(object sender, RoutedEventArgs e)
        {
            var w = new MixingWindow();   // <-- όπως στο screenshot
            w.Show();
            Close();
        }

        private void OpenMastering_Click(object sender, RoutedEventArgs e)
        {
            var w = new MainWindow();     // <-- το άλλο section που έχεις έτοιμο
            w.Show();
            Close();
        }
    }
}