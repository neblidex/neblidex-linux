/*
 * Created by SharpDevelop.
 * User: David
 * Date: 2/14/2018
 * Time: 2:15 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NebliDex
{
	/// <summary>
	/// Interaction logic for Deposit.xaml
	/// </summary>
	public partial class Deposit : Window
	{
		public Deposit()
		{
			InitializeComponent();
			WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
						
			for(int i = 0;i < App.WalletList.Count;i++){
				Coin_Box.Items.Add(App.WalletList[i].Coin);
			}
			Coin_Box.SelectedIndex = 0;
			string addre = App.WalletList[0].address;
			if(App.WalletList[0].blockchaintype == 4){
				addre = SharpCashAddr.Converter.ToCashAddress(addre);
			}
			
			Deposit_Address.Text = addre;

		}
		
		private void Close_Dialog(object sender, RoutedEventArgs e)
		{
			this.Close();
		}
		
		private void Change_Coin(object sender, SelectionChangedEventArgs e)
		{
			//First find which one was selected
			string which_coin = (string)Coin_Box.SelectedItem;
			
			string addre = "";
			for(int i = 0;i < App.WalletList.Count;i++){
				if(App.WalletList[i].Coin == which_coin){
					addre = App.WalletList[i].address;
					if(App.WalletList[i].blockchaintype == 4){ //If Bitcoin Cash
						addre = SharpCashAddr.Converter.ToCashAddress(addre); //Show the cash address for deposits
					}
					break;
				}
			}
			
			if(Deposit_Address != null){
				Deposit_Address.Text = addre;
			}
		}
	}
}