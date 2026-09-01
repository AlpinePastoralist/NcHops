using System.Windows;

namespace NCHops
{
    public partial class NullpunktDialog : Window
    {
        /// <summary>
        /// Index der ausgewählten Position (0-8)
        /// 0=ObenLinks, 1=ObenMitte, 2=ObenRechts,
        /// 3=MitteLinks, 4=Mitte, 5=MitteRechts,
        /// 6=UntenLinks, 7=UntenMitte, 8=UntenRechts
        /// </summary>
        public int SelectedPosition { get; private set; } = 6; // Standard: Unten Links

        public NullpunktDialog()
        {
            InitializeComponent();
        }

        private void OnPositionClicked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int position))
                {
                    SelectedPosition = position;
                    UpdateRadioButtons();

                    // Dialog direkt bestätigen und schließen
                    this.DialogResult = true;
                    this.Close();
                }
            }
        }

        private void UpdateRadioButtons()
        {
            RbObenLinks.IsChecked = (SelectedPosition == 0);
            RbObenMitte.IsChecked = (SelectedPosition == 1);
            RbObenRechts.IsChecked = (SelectedPosition == 2);
            RbMitteLinks.IsChecked = (SelectedPosition == 3);
            RbMitte.IsChecked = (SelectedPosition == 4);
            RbMitteRechts.IsChecked = (SelectedPosition == 5);
            RbUntenLinks.IsChecked = (SelectedPosition == 6);
            RbUntenMitte.IsChecked = (SelectedPosition == 7);
            RbUntenRechts.IsChecked = (SelectedPosition == 8);
        }
    }
}
