using System;
using System.Globalization;
using System.Threading.Tasks;
using Gtk;

namespace NebliDex_Linux
{
    public partial class MatchOrderWindow : Gtk.Window
    {
		public App.OpenOrder window_order;
        public decimal min_ord;

		public MatchOrderWindow(App.OpenOrder ord) :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
            //Old height is 365
			Gtk.Label match_label = (Gtk.Label)Match_Button.Children[0];
			match_label.Markup = "<span font='14'>Match</span>";
			My_Amount.KeyReleaseEvent += Amount_KeyUp;
			Total_Amount.KeyReleaseEvent += Total_KeyUp;
			Match_Button.Clicked += Match_Order;

			window_order = ord;

            string trade_symbol = App.MarketList[App.exchange_market].trade_symbol;
            string base_symbol = App.MarketList[App.exchange_market].base_symbol;

            min_ord = ord.minimum_amount;
            if (min_ord > ord.amount)
            {
                min_ord = ord.amount;
            }

            if (ord.type == 0)
            {
                //Buy Order
				Header.Markup = "<span font='18'><b>Buy Order Details</b></span>";
				Order_Type.Markup = "<span font='12'>Requesting:</span>";
				Order_Amount.Markup = "<span font='12'><b>" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", ord.amount)+" " + trade_symbol+"</b></span>";
				Order_Min_Amount.Markup = "<span font='12'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", min_ord)+" " + trade_symbol+"</b></span>";
				Price.Markup = "<span font='12'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", ord.price)+" " + base_symbol+"</b></span>";
                decimal my_balance = App.GetMarketBalance(App.exchange_market, 1); //If they are buying, we are selling

				My_Amount_Header.Markup = "<span font='11'>Amount (" + trade_symbol + "):</span>";
				Total_Cost_Header.Markup = "<span font='11'>Total Receive (" + base_symbol + "):</span>";
				My_Balance.Markup = "<span font='11'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", my_balance) + " " + trade_symbol+"</b></span>";

            }
            else
            {
                //Sell Order
				Header.Markup = "<span font='18'><b>Sell Order Details</b></span>";
				Order_Type.Markup = "<span font='12'>Available:</span>";
				Order_Amount.Markup = "<span font='12'><b>" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", ord.amount)+" " + trade_symbol+"</b></span>";
				Order_Min_Amount.Markup = "<span font='12'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", min_ord)+" " + trade_symbol+"</b></span>";
				Price.Markup = "<span font='12'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", ord.price)+" " + base_symbol+"</b></span>";
                decimal my_balance = App.GetMarketBalance(App.exchange_market, 0); //If they are selling, we are buying

				My_Amount_Header.Markup = "<span font='11'>Amount (" + trade_symbol + "):</span>";
				Total_Cost_Header.Markup = "<span font='11'>Total Cost (" + base_symbol + "):</span>";
				My_Balance.Markup = "<span font='11'><b>"+String.Format(CultureInfo.InvariantCulture, "{0:0.########}", my_balance) + " " + base_symbol+"</b></span>";
            }
        }

		private void Amount_KeyUp(object sender, EventArgs e)
        {
            Total_Amount.Text = "";
            if (App.IsNumber(My_Amount.Text) == false) { return; }
            decimal amount = decimal.Parse(My_Amount.Text, CultureInfo.InvariantCulture);
            if (amount <= 0) { return; }
            Total_Amount.Text = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", (amount * window_order.price));
        }

        private void Total_KeyUp(object sender, EventArgs e)
        {
            My_Amount.Text = "";
            if (App.IsNumber(Total_Amount.Text) == false) { return; }
            decimal total = decimal.Parse(Total_Amount.Text, CultureInfo.InvariantCulture);
            if (total <= 0) { return; }
            My_Amount.Text = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", (total / window_order.price));
        }

		private async void Match_Order(object sender, EventArgs e)
        {

            if (My_Amount.Text.Length == 0 || Total_Amount.Text.Length == 0) { return; }
            if (My_Amount.Text.IndexOf(",",StringComparison.InvariantCulture) >= 0)
            {
				App.MessageBox(this, "Notice", "NebliDex does not recognize commas for decimals at this time.", "OK");
                return;
            }
            if (Total_Amount.Text.IndexOf(",",StringComparison.InvariantCulture) >= 0)
            {
				App.MessageBox(this, "Notice", "NebliDex does not recognize commas for decimals at this time.", "OK");
                return;
            }
            decimal amount = decimal.Parse(My_Amount.Text, CultureInfo.InvariantCulture);
            decimal total = decimal.Parse(Total_Amount.Text, CultureInfo.InvariantCulture);
            if (amount < min_ord)
            {
                //Cannot be less than the minimum order
				App.MessageBox(this, "Notice", "Amount cannot be less than the minimum match.", "OK");
                return;
            }
            if (amount > window_order.amount)
            {
                //Cannot be greater than request
				App.MessageBox(this, "Notice", "Amount cannot be greater than the order.", "OK");
                return;
            }

            if (App.MarketList[App.exchange_market].base_wallet == 3 || App.MarketList[App.exchange_market].trade_wallet == 3)
            {
                //Make sure amount is greater than ndexfee x 2
                if (amount < App.ndex_fee * 2)
                {
					App.MessageBox(this, "Notice", "This order amount is too small. Must be at least twice the CN fee (" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", App.ndex_fee * 2) + " NDEX)", "OK");
                    return;
                }
            }

            string msg = "";
            decimal mybalance = 0;
            int mywallet = 0;
            //Now check the balances
            if (window_order.type == 1)
            { //They are selling, so we are buying
                mybalance = total; //Base pair balance
                mywallet = App.MarketList[window_order.market].base_wallet; //This is the base pair wallet
            }
            else
            { //They are buying so we are selling
                mybalance = amount; //Base pair balance
                mywallet = App.MarketList[window_order.market].trade_wallet; //This is the trade pair wallet                
            }
            bool good = App.CheckWalletBalance(mywallet, mybalance, ref msg);
            if (good == true)
            {
                //Now check the fees
                good = App.CheckMarketFees(App.exchange_market, 1 - window_order.type, mybalance, ref msg, true);
            }

            if (good == false)
            {
                //Not enough funds or wallet unavailable
				App.MessageBox(this, "Notice", msg, "OK");
                return;
            }

            //Make sure that total is greater than block rates for both markets
            decimal block_fee1 = 0;
            decimal block_fee2 = 0;
            if (App.MarketList[App.exchange_market].trade_wallet > 2 || App.MarketList[App.exchange_market].trade_wallet == 0)
            {
                block_fee1 = App.blockchain_fee[0]; //Neblio fee
            }
            if (App.MarketList[App.exchange_market].base_wallet >= 0 && App.MarketList[App.exchange_market].base_wallet < 3)
            {
                block_fee2 = App.blockchain_fee[App.MarketList[App.exchange_market].base_wallet]; //Base fee
            }
            if (total < block_fee1 || total < block_fee2 || amount < block_fee1 || amount < block_fee2)
            {
                //The trade amount is too small
				App.MessageBox(this, "Notice", "This trade amount is too small to match because it is lower than the blockchain fee.", "OK");
                return;
            }

            //Because tokens are indivisible at the moment, amounts can only be in whole numbers
            if (App.MarketList[App.exchange_market].trade_wallet > 2)
            {
                if (Math.Abs(Math.Round(amount) - amount) > 0)
                {
					App.MessageBox(this, "Notice", "All NTP1 tokens are indivisible at this time. Must be whole amounts.", "OK");
                    return;
                }
                amount = Math.Round(amount);
            }

            //Cannot match order when another order is involved deeply in trade
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
				App.MessageBox(this, "Notice", "Another order is currently involved in trade. Please wait and try again.", "OK");
                return;
            }

            //Check to see if any other open orders of mine
            if (App.MyOpenOrderList.Count >= App.total_markets)
            {
				App.MessageBox(this, "Notice", "You have exceed the maximum amount (" + App.total_markets + ") of open orders.", "OK");
                return;
            }

            //Everything is good, create the request now
            //This will be a match open order (different than a general order)
            App.OpenOrder ord = new App.OpenOrder();
            ord.is_request = true; //Match order
            ord.order_nonce = window_order.order_nonce;
            ord.market = window_order.market;
            ord.type = 1 - window_order.type; //Opposite of the original order type
            ord.price = window_order.price;
            ord.amount = amount;
            ord.original_amount = amount;
            ord.order_stage = 0;
            ord.my_order = true; //Very important, it defines how much the program can sign automatically

            //Try to submit order request to CN
			Gtk.Label match_label = (Gtk.Label)Match_Button.Children[0];
			match_label.Markup = "<span font='14'>Contacting CN...</span>";
            Match_Button.Sensitive = false; //This will allow us to wait longer as user is notified
            bool worked = await Task.Run(() => App.SubmitMyOrderRequest(ord));
            if (worked == true)
            {
                //Add to lists and close form
                if (App.MyOpenOrderList.Count > 0)
                {
                    //Close all the other open orders until this one is finished
                    await Task.Run(() => App.QueueAllOpenOrders());
                }

                lock (App.MyOpenOrderList)
                {
                    App.MyOpenOrderList.Add(ord); //Add to our own personal list
                }
                ExchangeWindow.PendOrder(ord.order_nonce);
				Application.Invoke(delegate
                {
					if (App.main_window_loaded == true)
                    {
                        App.main_window.Open_Order_List_Public.NodeStore.AddNode(ord);
                    }
					this.Destroy();
                });
                return;
            }
			Application.Invoke(delegate
            {
				match_label = (Gtk.Label)Match_Button.Children[0];
				match_label.Markup = "<span font='14'>Match</span>";
                Match_Button.Sensitive = true;
            });
        }
    }
}
