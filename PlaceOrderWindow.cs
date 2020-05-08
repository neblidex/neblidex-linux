using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using Gtk;

namespace NebliDex_Linux
{
    public partial class PlaceOrderWindow : Gtk.Window
    {
		int order_type = 0;

        public PlaceOrderWindow(int type) :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
			this.Hide();
            //Old height is 335
			Gtk.Label order_label = (Gtk.Label)Order_Button.Children[0];
			order_label.Markup = "<span font='14'>Create Order</span>";
			Price_Input.KeyReleaseEvent += Price_KeyUp;
			Amount_Input.KeyReleaseEvent += Amount_KeyUp;
			Total_Input.KeyReleaseEvent += Total_KeyUp;
			Order_Button.Clicked += Make_Order;

			order_type = type;

            decimal balance = App.GetMarketBalance(App.exchange_market, type);

            decimal price = 0;
            if (App.ChartLastPrice[0].Count > 0)
            {

                //Get the last trade price for the market as default (on 24 hr chart)
                for (int i = App.ChartLastPrice[0].Count - 1; i >= 0; i--)
                {
                    if (App.ChartLastPrice[0][i].market == App.exchange_market)
                    {
                        price = App.ChartLastPrice[0][i].price; break;
                    }
                }
                Price_Input.Text = "" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", price);
            }

            string trade_symbol = App.MarketList[App.exchange_market].trade_symbol;
            string base_symbol = App.MarketList[App.exchange_market].base_symbol;

            if (type == 0)
            {
                //Buy Order
				Order_Header.Markup = "<span font='18'><b>Buy " + trade_symbol+"</b></span>";
				My_Balance.Markup = "<span font='12'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", balance) + " " + base_symbol+ "</b></span>";
				Price_Header.Markup= "<span font='11'>Price (" + base_symbol + "):</span>";
				Amount_Header.Markup = "<span font='11'>Amount (" + trade_symbol + "):</span>";
				Min_Amount_Header.Markup = "<span font='11'>Minimum Match (" + trade_symbol + "):</span>";
				Total_Header.Markup = "<span font='11'>Total Cost (" + base_symbol + "):</span>";
            }
            else
            {
                //Sell Order
				Order_Header.Markup = "<span font='18'><b>Sell " + trade_symbol+"</b></span>";
				My_Balance.Markup = "<span font='12'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", balance) + " " + trade_symbol+"</b></span>";
				Price_Header.Markup = "<span font='11'>Price (" + base_symbol + "):</span>";
				Amount_Header.Markup = "<span font='11'>Amount (" + trade_symbol + "):</span>";
				Min_Amount_Header.Markup = "<span font='11'>Minimum Match (" + trade_symbol + "):</span>";
				Total_Header.Markup = "<span font='11'>Total Receive (" + base_symbol + "):</span>";
            }
			this.Show();
        }

		private void Price_KeyUp(object sender, EventArgs e)
        {
            if (App.IsNumber(Price_Input.Text) == false) { return; }
            decimal price = decimal.Parse(Price_Input.Text, CultureInfo.InvariantCulture);
            if (price <= 0) { return; }

            if (App.IsNumber(Amount_Input.Text) == false) { return; }
            decimal amount = decimal.Parse(Amount_Input.Text, CultureInfo.InvariantCulture);
            if (amount <= 0) { return; }
            Total_Input.Text = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount * price);
        }

        private void Amount_KeyUp(object sender, EventArgs e)
        {
            if (App.IsNumber(Price_Input.Text) == false) { return; }
            decimal price = decimal.Parse(Price_Input.Text, CultureInfo.InvariantCulture);
            if (price <= 0) { return; }

            if (App.IsNumber(Amount_Input.Text) == false) { return; }
            decimal amount = decimal.Parse(Amount_Input.Text, CultureInfo.InvariantCulture);
            if (amount <= 0) { return; }
            Total_Input.Text = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount * price);
            //The default minimum
            decimal min_amount = amount / 100m;
			if (App.IsWalletNTP1(App.MarketList[App.exchange_market].trade_wallet) == true)
            {
                min_amount = Math.Round(min_amount); //Round to nearest whole number
                if (min_amount == 0) { min_amount = 1; }
            }
            Min_Amount_Input.Text = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", min_amount);
        }

        private void Total_KeyUp(object sender, EventArgs e)
        {
            if (App.IsNumber(Price_Input.Text) == false) { return; }
            decimal price = decimal.Parse(Price_Input.Text, CultureInfo.InvariantCulture);
            if (price <= 0) { return; }

            if (App.IsNumber(Total_Input.Text) == false) { return; }
            decimal total = decimal.Parse(Total_Input.Text, CultureInfo.InvariantCulture);
            if (total <= 0) { return; }
            decimal amount = total / price;
			if (App.IsWalletNTP1(App.MarketList[App.exchange_market].trade_wallet) == true)
            {
                amount = Math.Round(amount); //Round to nearest whole number
                if (amount == 0) { amount = 1; }
            }
            Amount_Input.Text = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount);
            decimal min_amount = amount / 100m;
			if (App.IsWalletNTP1(App.MarketList[App.exchange_market].trade_wallet) == true)
            {
                min_amount = Math.Round(min_amount); //Round to nearest whole number
                if (min_amount == 0) { min_amount = 1; }
            }
            Min_Amount_Input.Text = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", min_amount);
        }

		private async void Make_Order(object sender, EventArgs e)
        {
            //Create our order!
            //Get the price
            
            if (App.IsNumber(Price_Input.Text) == false) { return; }
            if (Price_Input.Text.IndexOf(",",StringComparison.InvariantCulture) >= 0)
            {
				App.MessageBox(this, "Notice","NebliDex does not recognize commas for decimals at this time.", "OK");
                return;
            }
            decimal price = decimal.Parse(Price_Input.Text, CultureInfo.InvariantCulture);
            if (price <= 0) { return; }
            if (price > App.max_order_price)
            {
                //Price cannot exceed the max
				App.MessageBox(this, "Notice","This price is higher than the maximum price of 10 000 000", "OK");
                return;
            }

            //Get the amount
            if (App.IsNumber(Amount_Input.Text) == false) { return; }
            if (Amount_Input.Text.IndexOf(",",StringComparison.InvariantCulture) >= 0)
            {
				App.MessageBox(this, "Notice","NebliDex does not recognize commas for decimals at this time.", "OK");
                return;
            }
            decimal amount = decimal.Parse(Amount_Input.Text, CultureInfo.InvariantCulture);
            if (amount <= 0) { return; }

            if (App.IsNumber(Min_Amount_Input.Text) == false) { return; }
            if (Min_Amount_Input.Text.IndexOf(",",StringComparison.InvariantCulture) >= 0)
            {
				App.MessageBox(this, "Notice","NebliDex does not recognize commas for decimals at this time.", "OK");
                return;
            }
            decimal min_amount = decimal.Parse(Min_Amount_Input.Text, CultureInfo.InvariantCulture);
            if (min_amount <= 0)
            {
				App.MessageBox(this, "Notice","The minimum amount is too small.", "OK");
                return;
            }
            if (min_amount > amount)
            {
				App.MessageBox(this, "Notice","The minimum amount cannot be greater than the amount.", "OK");
                return;
            }

            decimal total = Math.Round(price * amount, 8);
            if (Total_Input.Text.IndexOf(",",StringComparison.InvariantCulture) >= 0)
            {
				App.MessageBox(this, "Notice","NebliDex does not recognize commas for decimals at this time.", "OK");
                return;
            }

            if (App.MarketList[App.exchange_market].base_wallet == 3 || App.MarketList[App.exchange_market].trade_wallet == 3)
            {
                //Make sure amount is greater than ndexfee x 2
                if (amount < App.ndex_fee * 2)
                {
					App.MessageBox(this, "Notice","This order amount is too small. Must be at least twice the CN fee (" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", App.ndex_fee * 2) + " NDEX)", "OK");
                    return;
                }
            }

            int wallet = 0;
            string msg = "";
            bool good = false;
            if (order_type == 0)
            {
                //This is a buy order we are making, so we need base market balance
                wallet = App.MarketList[App.exchange_market].base_wallet;
                good = App.CheckWalletBalance(wallet, total, ref msg);
                if (good == true)
                {
                    //Now check the fees
                    good = App.CheckMarketFees(App.exchange_market, order_type, total, ref msg, false);
                }
            }
            else
            {
                //Selling the trade wallet amount
                wallet = App.MarketList[App.exchange_market].trade_wallet;
                good = App.CheckWalletBalance(wallet, amount, ref msg);
                if (good == true)
                {
                    good = App.CheckMarketFees(App.exchange_market, order_type, amount, ref msg, false);
                }
            }

            //Show error messsage if balance not available
            if (good == false)
            {
                //Not enough funds or wallet unavailable
				App.MessageBox(this, "Notice",msg, "OK");
                return;
            }
            
			//Make sure that total is greater than blockrate for the base market and the amount is greater than blockrate for trade market
            decimal block_fee1 = 0;
            decimal block_fee2 = 0;
            int trade_wallet_blockchaintype = App.GetWalletBlockchainType(App.MarketList[App.exchange_market].trade_wallet);
            int base_wallet_blockchaintype = App.GetWalletBlockchainType(App.MarketList[App.exchange_market].base_wallet);
            block_fee1 = App.blockchain_fee[trade_wallet_blockchaintype];
            block_fee2 = App.blockchain_fee[base_wallet_blockchaintype];

            //Now calculate the totals for ethereum blockchain
            if (trade_wallet_blockchaintype == 6)
            {
				block_fee1 = App.GetEtherContractTradeFee(App.Wallet.CoinERC20(App.MarketList[App.exchange_market].trade_wallet));
				if (App.Wallet.CoinERC20(App.MarketList[App.exchange_market].trade_wallet) == true)
                {
                    block_fee1 = Convert.ToDecimal(App.double_epsilon); // The minimum trade size for ERC20 tokens
                }
            }
            if (base_wallet_blockchaintype == 6)
            {
				block_fee2 = App.GetEtherContractTradeFee(App.Wallet.CoinERC20(App.MarketList[App.exchange_market].base_wallet));
				if (App.Wallet.CoinERC20(App.MarketList[App.exchange_market].base_wallet) == true)
                {
                    block_fee2 = Convert.ToDecimal(App.double_epsilon); // The minimum trade size for ERC20 tokens
                }
            }

            if (total < block_fee2 || amount < block_fee1)
            {
                //The trade amount is too small
				App.MessageBox(this, "Notice", "This trade amount is too small to create because it is lower than the blockchain fee.", "OK");
                return;
            }

			//ERC20 only check
			//We need to check if the ERC20 token contract allows us to pull tokens to the atomic swap contract
            bool sending_erc20 = false;
            decimal erc20_amount = 0;
            int erc20_wallet = 0;
            if (order_type == 0 && App.Wallet.CoinERC20(App.MarketList[App.exchange_market].base_wallet) == true)
            {
                //Buying trade with ERC20
                sending_erc20 = true;
                erc20_amount = total;
                erc20_wallet = App.MarketList[App.exchange_market].base_wallet;
            }
            else if (order_type == 1 && App.Wallet.CoinERC20(App.MarketList[App.exchange_market].trade_wallet) == true)
            {
                //Selling trade that is also an ERC20
                sending_erc20 = true;
                erc20_amount = amount;
                erc20_wallet = App.MarketList[App.exchange_market].trade_wallet;
            }

            if (sending_erc20 == true)
            {
				//And now look at all your other open orders on the exhange that send this token
                lock(App.MyOpenOrderList){
                    for(int i = 0; i < App.MyOpenOrderList.Count; i++){
                        if(App.MyOpenOrderList[i].type == 0 && App.MarketList[App.MyOpenOrderList[i].market].base_wallet == erc20_wallet){
                            //We are sending this token in another order, add it to the erc20_amount
                            erc20_amount += Math.Round(App.MyOpenOrderList[i].price*App.MyOpenOrderList[i].amount,8);
                        }else if(App.MyOpenOrderList[i].type == 1 && App.MarketList[App.MyOpenOrderList[i].market].trade_wallet == erc20_wallet){
                            erc20_amount += App.MyOpenOrderList[i].amount;
                        }
                    }
                }   

                //Make sure the allowance is there already
                decimal allowance = App.GetERC20AtomicSwapAllowance(App.GetWalletAddress(erc20_wallet), App.ERC20_ATOMICSWAP_ADDRESS, erc20_wallet);
                if (allowance < 0)
                {
					App.MessageBox(this, "Notice", "Error determining ERC20 token contract allowance, please try again.", "OK");
                    return;
                }
                else if (allowance < erc20_amount)
                {
                    //We need to increase the allowance to send to the atomic swap contract eventually
					bool result = App.PromptUser(this, "Confirmation", "Permission is required from this token's contract to send this amount to the NebliDex atomic swap contract.", "OK", "Cancel");
					if (result == true)
					{
                        //Create a transaction with this permission to send up to this amount
                        allowance = 1000000; //1 million tokens by default
                        if (erc20_amount > allowance) { allowance = erc20_amount; }
                        App.CreateAndBroadcastERC20Approval(erc20_wallet, allowance, App.ERC20_ATOMICSWAP_ADDRESS);
						Application.Invoke(delegate
                        {
							App.MessageBox(this, "Notice", "Now please wait for your approval to be confirmed by the Ethereum network then try again.", "OK");
                        });
                    }
                    return;
                }
            }

            //Because tokens are indivisible at the moment, amounts can only be in whole numbers
			bool ntp1_wallet = App.IsWalletNTP1(App.MarketList[App.exchange_market].trade_wallet);
            if (ntp1_wallet == true)
            {
                if (Math.Abs(Math.Round(amount) - amount) > 0)
                {
					App.MessageBox(this, "Notice","All NTP1 tokens are indivisible at this time. Must be whole amounts.", "OK");
                    return;
                }
                amount = Math.Round(amount);

                if (Math.Abs(Math.Round(min_amount) - min_amount) > 0)
                {
					App.MessageBox(this, "Notice","All NTP1 tokens are indivisible at this time. Must be whole minimum amounts.", "OK");
                    return;
                }
                min_amount = Math.Round(min_amount);
            }

            //Check to see if any other open orders of mine
            if (App.MyOpenOrderList.Count >= App.total_markets)
            {
				App.MessageBox(this, "Notice","You have exceed the maximum amount (" + App.total_markets + ") of open orders.", "OK");
                return;
            }

            App.OpenOrder ord = new App.OpenOrder();
            ord.order_nonce = App.GenerateHexNonce(32);
            ord.market = App.exchange_market;
            ord.type = order_type;
            ord.price = Math.Round(price, 8);
            ord.amount = Math.Round(amount, 8);
            ord.minimum_amount = Math.Round(min_amount, 8);
            ord.original_amount = amount;
            ord.order_stage = 0;
            ord.my_order = true; //Very important, it defines how much the program can sign automatically

            //Try to submit order to CN
            Order_Button.Sensitive = false;
			Gtk.Label order_label = (Gtk.Label)Order_Button.Children[0];
			order_label.Markup = "<span font='14'>Contacting CN...</span>";
			bool worked = await Task.Run(() => App.SubmitMyOrder(ord, null));
            if (worked == true)
            {
                //Add to lists and close order
                lock (App.MyOpenOrderList)
                {
                    App.MyOpenOrderList.Add(ord); //Add to our own personal list
                }
                lock (App.OpenOrderList[App.exchange_market])
                {
                    App.OpenOrderList[App.exchange_market].Add(ord);
                }
                App.main_window.AddOrderToView(ord);
				App.AddSavedOrder(ord);
				Application.Invoke(delegate
                {
					if(App.main_window_loaded == true){
						App.main_window.Open_Order_List_Public.NodeStore.AddNode(ord);
					}
                    this.Destroy();
                });
                return;
            }
			Application.Invoke(delegate
            {
				Order_Button.Sensitive = true;
                order_label = (Gtk.Label)Order_Button.Children[0];
				order_label.Markup = "<span font='14'>Create Order</span>";
            });
        }
    }
}
