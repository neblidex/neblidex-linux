using System;
using System.Numerics;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using NBitcoin;
using Nethereum;
using Gtk;

namespace NebliDex_Linux
{
    public partial class WithdrawWindow : Gtk.Window
    {
        public WithdrawWindow() :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
			this.Show();
            //Old Window height is 340
			Gtk.Label withdraw_label = (Gtk.Label)Withdraw_Button.Children[0];
            withdraw_label.Markup = "<span font='14'>Withdraw</span>";
            
            for (int i = 0; i < App.WalletList.Count; i++)
            {
                Coin_Box.AppendText(App.WalletList[i].Coin);
            }
			Coin_Box.Active = 0;
			decimal balance = App.WalletList[0].balance;

			Gtk.CellRendererText my_rend = (Gtk.CellRendererText)Coin_Box.Cells[0];
            my_rend.Scale = 1.4;
			Withdraw_Button.Clicked += Confirm_Withdraw_Firststep;
			Coin_Box.Changed += Change_Coin;

			Balance_Amount.Markup = "<span font='14'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", balance) + " "+App.WalletList[0].Coin+"</b></span>";
			this.Show();
		}

		private async void Confirm_Withdraw_Firststep(object sender, EventArgs e)
        {
            if (App.running_consolidation_check == true)
            {
                //Advise user to wait while wallet is performing consolidation check
				App.MessageBox(this, "Notice","Wallet is currently performing consolidation check. Please try again soon.", "OK");
                return;
            }

            if (App.my_wallet_pass.Length > 0)
            {
				UserPromptWindow p = new UserPromptWindow("Please enter your wallet password\nto withdraw.", true); //Window
                p.Parent = this;
                p.Modal = true;
                p.waiting = new ManualResetEvent(false);
                p.Show();

                //Now create a task and wait for it to return until after the form is closed
                await Task.Run(() => { p.waiting.WaitOne(); });
                if (p.final_response.Equals(App.my_wallet_pass) == false)
                {
					Application.Invoke(delegate
                    {
						App.MessageBox(this, "Notice", "You've entered an incorrect password.", "OK");
                    });
					return;
                }
            }

            //Now run the second step for Withdrawing
			Application.Invoke(delegate
            {
				//Because of the await, we are running on another thread
				//Go back to the UI thread
				Confirm_Withdraw_Secondstep();
            });
         
        }

		private async void Confirm_Withdraw_Secondstep()
		{
			if (App.IsNumber(Amount_Input.Text) == false) { return; }
            if (Amount_Input.Text.IndexOf(",", StringComparison.InvariantCulture) >= 0)
            {
                App.MessageBox(this, "Notice", "NebliDex does not recognize commas for decimals at this time.", "OK");
                return;
            }

            decimal amount = Math.Round(decimal.Parse(Amount_Input.Text, CultureInfo.InvariantCulture), 8);
            if (amount <= 0) { return; }

            string destination = Destination.Text.Trim();
            if (destination.Length == 0) { return; }

            int mywallet = 0;
            string which_coin = (string)Coin_Box.ActiveText;
            for (int i = 0; i < App.WalletList.Count; i++)
            {
                if (App.WalletList[i].Coin == which_coin)
                {
                    mywallet = App.WalletList[i].type;
                    break;
                }
            }

            //Now check the balance
            string msg = "";
            bool good = App.CheckWalletBalance(mywallet, amount, ref msg);
            if (good == false)
            {
                //Not enough funds or wallet unavailable
                App.MessageBox(this, "Notice", msg, "OK");
                return;
            }
                        
			//If sending out tokens, make sure that account has enough NEBL for gas
            int wallet_blockchain = App.GetWalletBlockchainType(mywallet);
            if (wallet_blockchain == 0 && App.IsWalletNTP1(mywallet) == true)
            {
                decimal nebl_bal = App.GetWalletAmount(0);
                if (nebl_bal < App.blockchain_fee[0] * 3)
                {
                    //We need at least 0.00033 to send out tokens
					App.MessageBox(this, "Notice", "You do not have enough NEBL (" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", App.blockchain_fee[0] * 3) + " NEBL) to withdraw tokens!", "OK");
                    return;
                }
            }
            else
            {
                if (wallet_blockchain != 6)
                {
                    //Make sure what we are sending is greater than the dust balance
                    if (amount < App.dust_minimum[wallet_blockchain])
                    {
						App.MessageBox(this, "Notice", "This amount is too small to send as it is lower than the dust minimum", "OK");
                        return;
                    }
                }
                else
                {
                    //Ethereum
					decimal eth_bal = App.GetWalletAmount(17);
                    if (eth_bal <= App.GetEtherWithdrawalFee(App.Wallet.CoinERC20(mywallet)))
                    {
						App.MessageBox(this, "Notice", "Your ETH balance is too small as it is lower than the gas fee to send this amount", "OK");
                        return;
                    }
                }
            }

            bool too_soon = false;
            lock (App.MyOpenOrderList)
            {
                for (int i = 0; i < App.MyOpenOrderList.Count; i++)
                {
					if (App.MyOpenOrderList[i].order_stage > 0) { too_soon = true; break; } //Your maker order is matching something
                    if (App.MyOpenOrderList[i].is_request == true) { too_soon = true; break; } //Already have another taker order
                }
            }

            if (too_soon == true)
            {
                App.MessageBox(this, "Notice", "An order is currently involved in trade. Please wait and try again.", "OK");
                return;
            }

            string suffix = " " + (string)Coin_Box.ActiveText;

            bool result = App.PromptUser(this, "Confirmation", "Are you sure you want to send " + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount) + suffix + " to " + destination + "?", "Yes", "Cancel");
            if (result == true)
            {

                //Queue all the open orders if any present
                if (App.MyOpenOrderList.Count > 0)
                {
                    await Task.Run(() => App.QueueAllOpenOrders());
                }

				if (wallet_blockchain == 4)
                {
                    //Bitcoin Cash
                    //Try to convert to old address, exception will be thrown if already old address
                    try
                    {
                        destination = SharpCashAddr.Converter.ToOldAddress(destination);
                    }
                    catch (Exception) { }
                }

                //Make sure to run in another thread
				Application.Invoke(delegate
                {
					Withdraw_Button.Sensitive = false;
                });
                
                bool ok = await Task.Run(() => PerformWithdrawal(mywallet, amount, destination));
                if (ok == true)
                {
                    this.Destroy();
                }
                else
                {
					Application.Invoke(delegate
                    {
						App.MessageBox(this, "Error!", "Failed to create a transaction!", "OK");
                        Withdraw_Button.Sensitive = true;
                    });
                }
            }		
		}

        private bool PerformWithdrawal(int wallet, decimal amount, string des)
        {
			int blockchain = App.GetWalletBlockchainType(wallet);
            if (blockchain != 6)
            {
                Transaction tx = App.CreateSignedP2PKHTx(wallet, amount, des, true, false);
                //Then add to database
                if (tx != null)
                {
                    //Now write to the transaction log
                    App.AddMyTxToDatabase(tx.GetHash().ToString(), App.GetWalletAddress(wallet), des, amount, wallet, 2, -1); //Withdrawal
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                //This is Ethereum transaction out
                //This function will estimate the gas used if sending to a contract
				Nethereum.Signer.TransactionChainId tx = null;
                if (App.Wallet.CoinERC20(wallet) == true)
                {
                    //ERC20 tokens only move amounts around at a specific contract, tokens are stored in the contract but allocated to the address
                    string token_contract = App.GetWalletERC20TokenContract(wallet);
                    BigInteger int_amount = App.ConvertToERC20Int(amount, App.GetWalletERC20TokenDecimals(wallet));
                    string transfer_data = App.GenerateEthereumERC20TransferData(des, int_amount);
                    tx = App.CreateSignedEthereumTransaction(wallet, token_contract, amount, false, 0, transfer_data);
                }
                else
                {
                    //This function will estimate the gas used if sending to a contract
                    tx = App.CreateSignedEthereumTransaction(wallet, des, amount, false, 0, "");
                }
                //Then add to database
                if (tx != null)
                {
                    //Broadcast this transaction, and write to log regardless of whether it returns a hash or not
                    //Now write to the transaction log
                    bool timeout;
                    App.TransactionBroadcast(wallet, tx.Signed_Hex, out timeout);
                    if (timeout == false)
                    {
                        App.UpdateWalletStatus(wallet, 2); //Set to wait
                        App.AddMyTxToDatabase(tx.HashID, App.GetWalletAddress(wallet), des, amount, wallet, 2, -1); //Withdrawal
                        return true;
                    }
                    else
                    {
                        App.NebliDexNetLog("Transaction broadcast timed out, not connected to internet");
                    }
                }
                return false;
            }
        }

        private void Change_Coin(object sender, EventArgs e)
        {
			//First find which one was selected
			string which_coin = (string)Coin_Box.ActiveText;

            decimal balance = 0;
            for (int i = 0; i < App.WalletList.Count; i++)
            {
                if (App.WalletList[i].Coin == which_coin)
                {
                    balance = App.WalletList[i].balance;
                    if (Balance_Amount != null)
                    {
						Balance_Amount.Markup = "<span font='14'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", balance) + " " + App.WalletList[i].Coin+"</b></span>";
                    }
                    break;
                }
            }
        }
    }
}
