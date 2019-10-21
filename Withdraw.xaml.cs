/*
 * Created by SharpDevelop.
 * User: David
 * Date: 2/14/2018
 * Time: 2:58 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Globalization;
using NBitcoin;
using Nethereum;

namespace NebliDex
{
	/// <summary>
	/// Interaction logic for Withdraw.xaml
	/// </summary>
	public partial class Withdraw : Window
	{
		public Withdraw()
		{
			InitializeComponent();
			WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
			
			
			for(int i = 0;i < App.WalletList.Count;i++){
				Coin_Box.Items.Add(App.WalletList[i].Coin);
			}
			Coin_Box.SelectedIndex = 0;
			decimal balance = App.WalletList[0].balance;
			
			Balance_Amount.Content = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",balance)+" "+App.WalletList[0].Coin;
		}
		
		private async void Confirm_Withdraw(object sender, RoutedEventArgs e)
		{
			if(App.running_consolidation_check == true){
				//Advise user to wait while wallet is performing consolidation check
        		MessageBox.Show("Wallet is currently performing consolidation check. Please try again soon.","Notice!",MessageBoxButton.OK);
        		return;				
			}
			
			if(App.my_wallet_pass.Length > 0){
			    UserPrompt p = new UserPrompt("Please enter your wallet password\nto withdraw.",true); //Window
			    p.Owner = this;
			    p.ShowDialog();
			    if(p.final_response.Equals(App.my_wallet_pass) == false){
			    	System.Windows.MessageBox.Show("You've entered an incorrect password.");
					return;
			    }
			}
			    
			if(App.IsNumber(Amount_Input.Text) == false){return;}
        	if(Amount_Input.Text.IndexOf(",") >= 0){
        		MessageBox.Show("NebliDex does not recognize commas for decimals at this time.","Notice!",MessageBoxButton.OK);
        		return;
        	}
			
			decimal amount = Math.Round(decimal.Parse(Amount_Input.Text,CultureInfo.InvariantCulture),8);
			if(amount <= 0){return;}
			
			string destination = Destination.Text.Trim();
			if(destination.Length == 0){return;}
			
			int mywallet = 0;
			string which_coin = (string)Coin_Box.SelectedItem;
			for(int i = 0;i < App.WalletList.Count;i++){
				if(App.WalletList[i].Coin == which_coin){
					mywallet = App.WalletList[i].type;
					break;
				}
			}
			
			//Now check the balance
			string msg="";
			bool good = App.CheckWalletBalance(mywallet,amount,ref msg);
			if(good == false){
				//Not enough funds or wallet unavailable
				MessageBox.Show(msg,"Notice!",MessageBoxButton.OK);
				return;
			}
			
			//If sending out tokens, make sure that account has enough NEBL for gas
			int wallet_blockchain = App.GetWalletBlockchainType(mywallet);
			if(wallet_blockchain == 0 && App.IsWalletNTP1(mywallet) == true){
				decimal nebl_bal = App.GetWalletAmount(0);
				if(nebl_bal < App.blockchain_fee[0]*3){
					//We need at least 0.00033 to send out tokens
					MessageBox.Show("You do not have enough NEBL ("+String.Format(CultureInfo.InvariantCulture,"{0:0.########}",App.blockchain_fee[0]*3)+" NEBL) to withdraw tokens!","Notice!",MessageBoxButton.OK);
					return;						
				}
			}else{
				if(wallet_blockchain != 6){
					//Make sure what we are sending is greater than the dust balance
					if(amount < App.dust_minimum[wallet_blockchain]){
						MessageBox.Show("This amount is too small to send as it is lower than the dust minimum","Notice!",MessageBoxButton.OK);
						return;		
					}
				}else{
					//Ethereum
					decimal eth_bal = App.GetWalletAmount(17);
					if(eth_bal <= App.GetEtherWithdrawalFee(App.Wallet.CoinERC20(mywallet))){
						MessageBox.Show("Your ETH balance is too small as it is lower than the gas fee to send this amount","Notice!",MessageBoxButton.OK);
						return;						
					}
				}
			}
			
        	bool too_soon = false;
        	lock(App.MyOpenOrderList){
				for(int i = 0;i < App.MyOpenOrderList.Count;i++){
        			if(App.MyOpenOrderList[i].order_stage > 0){ too_soon = true; break; } //Your maker order is matching something
        			if(App.MyOpenOrderList[i].is_request == true){ too_soon = true; break; } //Already have another taker order
				}
        	}
        	
        	if(too_soon == true){
				MessageBox.Show("An order is currently involved in trade. Please wait and try again.","Notice!",MessageBoxButton.OK);
				return;        		
        	}
			
			string suffix = " "+(string)Coin_Box.SelectedItem;
						
			MessageBoxResult result = MessageBox.Show("Are you sure you want to send "+String.Format(CultureInfo.InvariantCulture,"{0:0.########}",amount)+suffix+" to "+destination+"?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (result == MessageBoxResult.Yes)
			{
				
				//Queue all the open orders if any present
				if(App.MyOpenOrderList.Count > 0){
					await Task.Run(() => App.QueueAllOpenOrders()  );
				}

				if(wallet_blockchain == 4){
					//Bitcoin Cash
					//Try to convert to old address, exception will be thrown if already old address
					try{
						destination = SharpCashAddr.Converter.ToOldAddress(destination);
					}catch(Exception){}
				}
				
			    //Make sure to run in another thread
			    Withdraw_Button.IsEnabled = false;
			    bool ok = await Task.Run(() => PerformWithdrawal(mywallet,amount,destination)  );
			    if(ok == true){
			    	this.Close();
			    }else{
			    	MessageBox.Show("Failed to create a transaction!");
			    	Withdraw_Button.IsEnabled = true;
			    }
			}		
		}
		
		private bool PerformWithdrawal(int wallet, decimal amount, string des)
		{
			int blockchain = App.GetWalletBlockchainType(wallet);
			if(blockchain != 6){
				Transaction tx = App.CreateSignedP2PKHTx(wallet,amount,des,true,false);
				//Then add to database
				if(tx!=null){
					//Now write to the transaction log
					App.AddMyTxToDatabase(tx.GetHash().ToString(),App.GetWalletAddress(wallet),des,amount,wallet,2,-1); //Withdrawal
					return true;
				}else{
					return false;
				}
			}else{
				//This is Ethereum transaction out
				
				Nethereum.Signer.TransactionChainId tx = null;
				if(App.Wallet.CoinERC20(wallet) == true){
					//ERC20 tokens only move amounts around at a specific contract, tokens are stored in the contract but allocated to the address
					string token_contract = App.GetWalletERC20TokenContract(wallet);
					BigInteger int_amount = App.ConvertToERC20Int(amount,App.GetWalletERC20TokenDecimals(wallet));
					string transfer_data = App.GenerateEthereumERC20TransferData(des,int_amount);
					tx = App.CreateSignedEthereumTransaction(wallet,token_contract,amount,false,0,transfer_data);
				}else{
					//This function will estimate the gas used if sending to a contract
					tx = App.CreateSignedEthereumTransaction(wallet,des,amount,false,0,"");
				}
				//Then add to database
				if(tx!=null){
					//Broadcast this transaction, and write to log regardless of whether it returns a hash or not
					//Now write to the transaction log
					bool timeout;					
					App.TransactionBroadcast(wallet,tx.Signed_Hex,out timeout);
					if(timeout == false){
						App.UpdateWalletStatus(wallet,2); //Set to wait
						App.AddMyTxToDatabase(tx.HashID,App.GetWalletAddress(wallet),des,amount,wallet,2,-1); //Withdrawal
						return true;
					}else{
						App.NebliDexNetLog("Transaction broadcast timed out, not connected to internet");
					}
				}
				return false;
			}
		}
		
		private void Change_Coin(object sender, SelectionChangedEventArgs e)
		{
			//First find which one was selected
			string which_coin = (string)Coin_Box.SelectedItem;
			
			decimal balance=0;
			for(int i = 0;i < App.WalletList.Count;i++){
				if(App.WalletList[i].Coin == which_coin){
					balance = App.WalletList[i].balance;
					if(Balance_Amount != null){
						Balance_Amount.Content = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",balance)+" "+App.WalletList[i].Coin;
					}
					break;
				}
			}
		}
	}
}