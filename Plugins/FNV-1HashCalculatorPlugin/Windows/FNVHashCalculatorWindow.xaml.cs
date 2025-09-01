using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Documents;
using Frosty.Controls;
using Frosty.Core;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers.Entries;
using FrostySdk.Resources;

namespace FNVHashCalculatorPlugin.Windows
{
    /// <summary>
    /// Interaction logic for FNVHashCalculatorWindow.xaml
    /// </summary>
    public partial class FNVHashCalculatorWindow : FrostyDockableWindow
    {
        private string mGUIDName = "";

        private string mHashOutput = "";

        public FNVHashCalculatorWindow()
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow;
        }

        private void GUIDNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            mGUIDName = varGUIDNameTextBox.Text;
        }

        private void CalculateHashButton_Click(object sender, RoutedEventArgs e)
        {
            
            if (mGUIDName.Length != 36)
            {
                App.Logger.Log("Please enter a valid GUID.");
            }
            else
            {
                uint hashValue = (uint)Utils.HashString(mGUIDName);
                varHashOutputTextBox.Text = hashValue.ToString();

                App.Logger.Log("Successfully calculated hash.");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
