using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Gtk;
 
namespace NebliDex_Linux
{
	
	public partial class App
	{
		
		//The trader API is a built-in server that responds to HTTP like queries formatted as JSON that can be used to modify
		//and create/remove open orders. It is designed to be used with bot traders and for market making.
		//NebliDex Android doesn't have this API
		
		public static HttpListener trader_api_server;
		public static bool trader_api_activated = false;
		public static int trader_api_port = 6328;
		public static bool trader_api_changing_markets = false; // This will be true if markets are changing
		
		public static async void SetTraderAPIServer(bool activate)
		{
			if(activate == false && trader_api_server != null){
				//We need to disconnect the trader_api_server
				trader_api_activated = false;
				trader_api_server.Stop(); //Will cause an exception from running server
				NebliDexNetLog("Trader API Server Stopped");
				trader_api_server = null;
				return;
			}
			
			if(activate == true && trader_api_server == null && critical_node == false){
				try {
					if(HttpListener.IsSupported == false){
						NebliDexNetLog("Trader API Server Not Supported");
						return;
					}
					trader_api_activated = true;
					trader_api_server = new HttpListener();
					trader_api_server.Prefixes.Add("http://localhost:"+trader_api_port+"/"); // Using 127.0.0.1 (not localhost) requires Admin access on Windows
					trader_api_server.Start();
					NebliDexNetLog("Trader API Server Activated...");
					while(trader_api_activated == true){
						//This status may change
						try {
							HttpListenerContext context = await trader_api_server.GetContextAsync(); // Wait for an HTTP request
							if(context != null){
								// Evaluate the HTTP request and respond
								#pragma warning disable
								Task.Run(() => ProcessTraderAPIRequest(context) );
								#pragma warning enable
							}						
						} catch (Exception e) {
							NebliDexNetLog("Failed to receive trader api http request, error: "+e.ToString());
						}
					}
					NebliDexNetLog("Trader API Server Stopped Listening...");
				} catch (Exception e) {
					NebliDexNetLog("Disconnected trader api server, error: "+e.ToString());
					if(trader_api_server.IsListening == true){
						trader_api_server.Stop();
					}
					trader_api_server = null;
					trader_api_activated = false;
				}
			}
		}
		
		public static void ProcessTraderAPIRequest(HttpListenerContext context)
		{
			HttpListenerRequest request_context = context.Request; // Get the request object
			HttpListenerResponse response = context.Response; // Get the response object for our eventual response
			System.IO.Stream output = response.OutputStream;
			string resp_message = "";
			try{
				
				// Server can read both GET (urlencoded) and POST (JSON) data, if GET data is blank, will read post data
				JObject js;
				if(request_context.QueryString.Count > 0){
					// There is some GET data, read it to JSON object
					js = new JObject();
					foreach(string key in request_context.QueryString.AllKeys){
						js[key] = request_context.QueryString[key];
					}
				}else{
					string req_message = "";
					using (StreamReader reader = new StreamReader(request_context.InputStream,request_context.ContentEncoding))
					{
					    req_message = reader.ReadToEnd();
					}
					// Formatted should be JSON encoded
					if(req_message.Length == 0){
						throw new Exception("Request content is empty");
					}
					js = JObject.Parse(req_message); //Parse this message					
				}
				
				JObject resp = new JObject();
				resp["error"] = true;
				int resp_code = 0;
				string request = js["request"].ToString();
				if(request == "ping"){
					// Ping / Pong, to test connection					
					resp["result"] = "Pong";
					resp["error"] = false;
					resp_code = 1;
				}else if(request == "currentMarket"){
					// Return the current market if not still loading
					resp["error"] = false;
					resp_code = 1;
					if(trader_api_changing_markets == false){
						resp["result"] = MarketList[exchange_market].trade_symbol + "/" + MarketList[exchange_market].base_symbol;
					}else{
						resp["result"] = "Loading Market";
					}
				}else if(request == "lastPrice"){
					// Returns the last price for the trade token
					if(trader_api_changing_markets == false){
						resp["error"] = false;
						resp_code = 1;
						resp["result"] = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",GetMarketLastPrice(exchange_market));						
					}else{
						resp_code = 22;
					}
				}else if(request == "currentCNFee"){
					// Returns the last price for the trade token
					resp["error"] = false;
					resp_code = 1;
					resp["result"] = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",ndex_fee);
				}else if(request == "marketList"){
					// Returns an array with a list of tradeable markets
					resp["error"] = false;
					resp_code = 1;
					JArray jarr = new JArray();
					for(int i=0;i < MarketList.Count;i++){
						if(MarketList[i].active == false){
							continue;
						}
						string format_market = MarketList[i].format_market;
						jarr.Add(format_market);
					}
					resp["result"] = jarr;
				}else if(request == "walletList"){
					// Returns an array with a list of different wallets
					resp["error"] = false;
					resp_code = 1;
					JArray jarr = new JArray();
					for(int i=0;i < WalletList.Count;i++){
						string coin = WalletList[i].Coin;
						jarr.Add(coin);
					}
					resp["result"] = jarr;
				}else if(request == "walletDetails"){
					// Returns information about a specific wallet
					resp["error"] = false;
					resp_code = 1;
					string walletcoin = js["coin"].ToString();
					JObject result = new JObject();
					walletcoin = walletcoin.ToLower(); // Make it case insensitive
					for(int i=0;i < WalletList.Count;i++){
						string coin = WalletList[i].Coin.ToLower();
						if(coin == walletcoin){
							result["balance"] = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",WalletList[i].balance);
							result["address"] = WalletList[i].address;
							if(WalletList[i].blockchaintype == 4){ // Bitcoin Cash converter
								resp["address"] = SharpCashAddr.Converter.ToCashAddress(WalletList[i].address);
							}
							if(WalletList[i].status == 0 || WalletList[i].status == 1){
								result["status"] = "Available";
							}else{
								result["status"] = "Not Available";
							}
							break;
						}
					}
					resp["result"] = result;
				}else if(request == "marketDepth"){
					// Returns an array with a list of different wallets
					if(trader_api_changing_markets == false){
						resp["error"] = false;
						resp_code = 1;
						JObject result = new JObject();
						JArray bids = TraderAPIAllOpenOrders(0);
						// Get buy data
						result["bids"] = bids;
						JArray asks = TraderAPIAllOpenOrders(1);
						// Get sell data
						result["asks"] = asks;
						resp["result"] = result;						
					}else{
						resp_code = 22;
					}
				}else if(request == "recentMarketTrades"){
					// Returns an array with a list of the most recent trades for the market
					if(trader_api_changing_markets == false){
						resp["error"] = false;
						resp_code = 1;
						JArray result = TraderAPIRecentTrades();
						resp["result"] = result;						
					}else{
						resp_code = 22;
					}
				}else if(request == "myOpenOrders"){
					// Returns an array with a list of open orders I have present
					resp["error"] = false;
					resp_code = 1;
					JArray result = TraderAPIMyOpenOrders();
					resp["result"] = result;
				}else if(request == "changeMarket"){
					// This will force change the currently displayed market
					bool can_change = false;
					if(trader_api_changing_markets == false){							
						can_change = true;
					}
					if(can_change == true){
						string target = js["desiredMarket"].ToString();
						bool ok = true;
						int which_market = TraderAPI_Selected_Market(target);
						if(which_market < 0){ok = false;}
						TraderAPIChangeMarket(target);
						if(ok == true){
							resp_code = 1;
							resp["error"] = false;
							if(run_headless == true){
								Console.WriteLine("Changing current market to "+target);
							}
						}else{
							resp_code = 2;
							resp["error"] = true;							
						}
					}else{
						resp_code = 2;
						resp["error"] = true;
					}
				}else if(request == "cancelOrder"){
					// Returns success if able to cancel order, otherwise a different code
					string myid = js["orderID"].ToString(); // Get the order nonce
					OpenOrder ord = null;
					lock(MyOpenOrderList){
						for(int i = 0; i < MyOpenOrderList.Count;i++){
							if(MyOpenOrderList[i].order_nonce.Equals(myid) == true){
								ord = MyOpenOrderList[i];break;
							}
						}
					}
					if(ord != null){
						if(ord.is_request == true){
							resp["error"] = true;
							resp_code = 17;							
						}else if(ord.order_stage >= 3){
							// Order involved in trade
							resp["error"] = true;
							resp_code = 3;
						}else{
							resp["error"] = false;
							resp_code = 1;
							CancelMyOrder(ord);
							if(run_headless == true){
								Console.WriteLine("Canceled my order");
							}
						}						
					}
				}else if(request == "cancelAllOrders"){
					// Returns success if able to cancel all orders
					List<OpenOrder> tempList = new List<OpenOrder>(MyOpenOrderList);
					bool allgood = true;
					for(int i = 0; i < tempList.Count;i++){
						if(tempList[i].order_stage >= 3 && tempList[i].is_request == false){
							allgood = false; // Someone is trading with you
							break;
						}
					}
					if(allgood == false){
						// Order involved in trade
						resp["error"] = true;
						resp_code = 4;
					}else{
						resp["error"] = false;
						resp_code = 1;
						for(int i = 0; i < tempList.Count;i++){
							if(tempList[i].is_request == false){
								CancelMyOrder(tempList[i]);
							}
						}
						if(run_headless == true){
							Console.WriteLine("Canceled all my maker orders");
						}
					}
				}else if(request == "postTakerOrder"){
					// Returns success if able to match the order and it will return the taker orderID
					// Info needed to match order:
					// Maker orderID
					// Amount to match
					// Autoapprove ERC20
					string orderID = js["orderID"].ToString();
					decimal amount = decimal.Parse(js["amount"].ToString(),CultureInfo.InvariantCulture);
					bool approve_erc20 = false;
					if(js["approveERC20"] != null){
						if(Convert.ToBoolean(js["approveERC20"].ToString()) == true){
							approve_erc20 = true;
						}
					}
					TraderAPITakerOrder(orderID, amount, approve_erc20, out resp_code);
					if(resp_code != 1){
						resp["error"] = true;
					}else{
						resp["error"] = false;
						if(run_headless == true){
							Console.WriteLine("Posted taker order to current market");
						}
					}
				}else if(request == "postMakerOrder"){
					// Returns success if able to match the order and it will return a new orderID
					// Info needed to make order:
					// Price
					// Amount
					// Min Amount
					// Order Type (buy vs sell)
					// Autoapprove ERC20
					decimal amount = decimal.Parse(js["amount"].ToString(),CultureInfo.InvariantCulture);
					decimal min_amount = decimal.Parse(js["minAmount"].ToString(),CultureInfo.InvariantCulture);
					decimal price = decimal.Parse(js["price"].ToString(),CultureInfo.InvariantCulture);
					int order_type = 0;
					string order_type_string = js["orderType"].ToString();
					if(order_type_string.ToLower() == "sell"){
						order_type = 1;
					}
					bool approve_erc20 = false;
					if(js["approveERC20"] != null){
						if(Convert.ToBoolean(js["approveERC20"].ToString()) == true){
							approve_erc20 = true;
						}
					}
					string orderID = "";
					TraderAPIMakerOrder(price, amount, min_amount, order_type, approve_erc20, out resp_code, out orderID);
					if(resp_code != 1){
						resp["error"] = true;
					}else{
						resp["error"] = false;
						resp["result"] = orderID;
						if(run_headless == true){
							Console.WriteLine("Posted maker order to current market");
						}
					}
				}
				
				resp["code"] = resp_code;
				resp["message"] = TraderAPICodeMessage(resp_code);
				resp_message = JsonConvert.SerializeObject(resp);
			}catch(Exception e){
				NebliDexNetLog("Malformed Trader API message, error: "+e.ToString());
			}finally{
				// Finally form the response message and send back
				// Output is formatted as JSON response, int code, string message, bool error, optional result object
				if(resp_message.Length > 0){
					byte[] buffer = System.Text.Encoding.UTF8.GetBytes(resp_message);
					response.ContentLength64 = buffer.Length;
					response.AddHeader("Content-Type","application/json"); // Tell response client return type
					response.AddHeader("Cache-Control","no-store"); // Tell response clients not to store result as it changes dynamically
					output.Write(buffer,0,buffer.Length);					
				}
				output.Close(); // Always close the output stream
			}
		}
		
		public static void TraderAPIMakerOrder(decimal price, decimal amount, decimal min_amount, int order_type, bool approve_erc20, out int code, out string orderID)
		{
			if(trader_api_changing_markets == true){
				code = 21;
				orderID = "";
				return;
			}
			if(price > max_order_price){
				code = 23;
				orderID = "";
				return;
			}
        	decimal total = Math.Round(price*amount,8);

        	if(MarketList[exchange_market].base_wallet == 3 || MarketList[exchange_market].trade_wallet == 3){
	        	//Make sure amount is greater than ndexfee x 2
	        	if(amount < ndex_fee*2){
	        		code = 8;
	        		orderID = "";
	        		return;
	        	}
        	}
        	
        	int wallet=0;
        	string msg="";
        	bool good = false;
        	if(order_type == 0){
        		//This is a buy order we are making, so we need base market balance
        		wallet = MarketList[exchange_market].base_wallet;
        		good = CheckWalletBalance(wallet,total,ref msg);
        		if(good == true){
        			//Now check the fees
        			good = CheckMarketFees(exchange_market,order_type,total,ref msg,false);
        		}
        	}else{
        		//Selling the trade wallet amount
        		wallet = MarketList[exchange_market].trade_wallet;
        		good = CheckWalletBalance(wallet,amount,ref msg);
        		if(good == true){
        			good = CheckMarketFees(exchange_market,order_type,amount,ref msg,false);
        		}
        	}
        	
			//Show error messsage if balance not available
			if(good == false){
				//Not enough funds or wallet unavailable
        		code = 9;
        		orderID = "";
				return;
			}
			
			//Make sure that total is greater than blockrate for the base market and the amount is greater than blockrate for trade market
			decimal block_fee1 = 0;
			decimal block_fee2 = 0;
			int trade_wallet_blockchaintype = GetWalletBlockchainType(MarketList[exchange_market].trade_wallet);
			int base_wallet_blockchaintype = GetWalletBlockchainType(MarketList[exchange_market].base_wallet);
			block_fee1 = blockchain_fee[trade_wallet_blockchaintype];
			block_fee2 = blockchain_fee[base_wallet_blockchaintype];
			
			//Now calculate the totals for ethereum blockchain
			
			//ERC20 tokens should not require a minimum trade amount (or maybe use double epsilon)
			
			if(trade_wallet_blockchaintype == 6){
				block_fee1 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[exchange_market].trade_wallet));
				if(Wallet.CoinERC20(MarketList[exchange_market].trade_wallet) == true){
					block_fee1 = Convert.ToDecimal(double_epsilon); // The minimum trade size for ERC20 tokens
				}
			}
			if(base_wallet_blockchaintype == 6){
				block_fee2 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[exchange_market].base_wallet));
				if(Wallet.CoinERC20(MarketList[exchange_market].base_wallet) == true){
					block_fee2 = Convert.ToDecimal(double_epsilon); // The minimum trade size for ERC20 tokens
				}
			}
			
			if(total < block_fee2 || amount < block_fee1){
				//The trade amount is too small
        		code = 10;
        		orderID = "";
				return;  				
			}
			
			//ERC20 only check
			bool sending_erc20 = false;
			decimal erc20_amount = 0;
			int erc20_wallet = 0;
			if(order_type == 0 && Wallet.CoinERC20(MarketList[exchange_market].base_wallet) == true){
				//Buying trade with ERC20
				sending_erc20 = true;
				erc20_amount = total;
				erc20_wallet = MarketList[exchange_market].base_wallet;
			}else if(order_type == 1 && Wallet.CoinERC20(MarketList[exchange_market].trade_wallet) == true){
				//Selling trade that is also an ERC20
				sending_erc20 = true;
				erc20_amount = amount;
				erc20_wallet = MarketList[exchange_market].trade_wallet;
			}
			
			if(sending_erc20 == true){
				//And now look at all your other open orders on the exhange that send this token
				lock(MyOpenOrderList){
					for(int i = 0; i < MyOpenOrderList.Count; i++){
						if(MyOpenOrderList[i].type == 0 && MarketList[MyOpenOrderList[i].market].base_wallet == erc20_wallet){
							//We are sending this token in another order, add it to the erc20_amount
							erc20_amount += Math.Round(MyOpenOrderList[i].price*MyOpenOrderList[i].amount,8);
						}else if(MyOpenOrderList[i].type == 1 && MarketList[MyOpenOrderList[i].market].trade_wallet == erc20_wallet){
							erc20_amount += MyOpenOrderList[i].amount;
						}
					}
				}				
				
				//Make sure the allowance is there already
				decimal allowance = GetERC20AtomicSwapAllowance(GetWalletAddress(erc20_wallet),ERC20_ATOMICSWAP_ADDRESS,erc20_wallet);
				if(allowance < 0){
	        		code = 11;
	        		orderID = "";
					return; 					
				}else if(allowance < erc20_amount){
					//We need to increase the allowance to send to the atomic swap contract eventually
					if(approve_erc20 == false){
		        		code = 12;
		        		orderID = "";
		        		return;
					}
					//Create a transaction with this permission to send up to this amount
					allowance = 1000000; //1 million tokens by default
					if(erc20_amount > allowance){allowance = erc20_amount;}
					CreateAndBroadcastERC20Approval(erc20_wallet,allowance,ERC20_ATOMICSWAP_ADDRESS);
	        		code = 13;
	        		orderID = "";	
					return; 										
				}
			}
			
        	//Because tokens are indivisible at the moment, amounts can only be in whole numbers
        	bool ntp1_wallet = IsWalletNTP1(MarketList[exchange_market].trade_wallet);
        	if(ntp1_wallet == true){
        		if(Math.Abs(Math.Round(amount)-amount) > 0){
	        		code = 14;
	        		orderID = "";
					return;        			
        		}
        		amount = Math.Round(amount);
        		
        		if(Math.Abs(Math.Round(min_amount)-min_amount) > 0){
	        		code = 20;
	        		orderID = "";
					return;        			
        		}
        		min_amount = Math.Round(min_amount);
        	}
			
			//Check to see if any other open orders of mine
			if(MyOpenOrderList.Count >= total_markets){
        		code = 16;
        		orderID = "";
				return;
			}
			
			OpenOrder ord = new OpenOrder();
			ord.order_nonce = GenerateHexNonce(32);
			ord.market = exchange_market;
			ord.type = order_type;
			ord.price = Math.Round(price,8);
			ord.amount = Math.Round(amount,8);
			ord.minimum_amount = Math.Round(min_amount,8);
			ord.original_amount = amount;
			ord.order_stage = 0;
			ord.my_order = true; //Very important, it defines how much the program can sign automatically
			
			//Try to submit order to CN
			bool worked = SubmitMyOrder(ord,null,true);
			if(worked == true){
				//Add to lists and close order
				lock(MyOpenOrderList){
					MyOpenOrderList.Add(ord); //Add to our own personal list
				}
				lock(OpenOrderList[exchange_market]){
					OpenOrderList[exchange_market].Add(ord);
				}
				if(main_window_loaded == true){
					main_window.AddOrderToView(ord);
					Application.Invoke(delegate
                    {
                        main_window.Open_Order_List_Public.NodeStore.AddNode(ord);
                    });			
				}
				AddSavedOrder(ord);
				code = 1;
				orderID = ord.order_nonce;
				return;
			}	
			code = 19;
			orderID = "";
		}
		
		public static void TraderAPITakerOrder(string orderID, decimal amount, bool approve_erc20, out int code)
		{
			if(trader_api_changing_markets == true){
				code = 21;
				return;
			}
			OpenOrder ord = null;
			lock(OpenOrderList[exchange_market]){
				for(int i = 0;i < OpenOrderList[exchange_market].Count;i++){
					if(OpenOrderList[exchange_market][i].order_nonce == orderID){
		    			ord = OpenOrderList[exchange_market][i];break;
		    		}
		    	}
		    }
			lock(MyOpenOrderList){
				for(int i = 0;i < MyOpenOrderList.Count;i++){
					if(MyOpenOrderList[i].order_nonce == orderID){
						// We are trying to match our own order, can't do.
		    			code = 18;
		    			return;
		    		}
		    	}
		    }
			if(ord == null){
				code = 5;
				return;
			}
			if(amount < ord.minimum_amount){
				code = 6;
				return;
			}
			
			if(amount > ord.amount){
				//Cannot be greater than request
				code = 7;
				return;
			}
			
        	if(MarketList[exchange_market].base_wallet == 3 || MarketList[exchange_market].trade_wallet == 3){
	        	//Make sure amount is greater than ndexfee x 2
	        	if(amount < ndex_fee*2){
					code = 8;
	        		return;
	        	}
        	}
			
			string msg="";
			decimal mybalance=0;
			int mywallet=0;
			decimal total = Math.Round(amount*ord.price,8);
			//Now check the balances
			if(ord.type == 1){ //They are selling, so we are buying
				mybalance = total; //Base pair balance
				mywallet = MarketList[ord.market].base_wallet; //This is the base pair wallet
			}else{ //They are buying so we are selling
				mybalance = amount; //Base pair balance
				mywallet = MarketList[ord.market].trade_wallet; //This is the trade pair wallet				
			}
			bool good = CheckWalletBalance(mywallet,mybalance,ref msg);
    		if(good == true){
    			//Now check the fees
    			good = CheckMarketFees(exchange_market,1 - ord.type,mybalance,ref msg,true);
    		}
			
			if(good == false){
				code = 9;
				//Not enough funds or wallet unavailable
				return;
			}
			
			//Make sure that total is greater than blockrate for the base market and the amount is greater than blockrate for trade market
			decimal block_fee1 = 0;
			decimal block_fee2 = 0;
			int trade_wallet_blockchaintype = GetWalletBlockchainType(MarketList[exchange_market].trade_wallet);
			int base_wallet_blockchaintype = GetWalletBlockchainType(MarketList[exchange_market].base_wallet);
			block_fee1 = blockchain_fee[trade_wallet_blockchaintype];
			block_fee2 = blockchain_fee[base_wallet_blockchaintype];
			
			//Now calculate the totals for ethereum blockchain
			//ERC20 tokens should not require a minimum trade amount (or maybe use double epsilon)
			
			if(trade_wallet_blockchaintype == 6){
				block_fee1 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[exchange_market].trade_wallet));
				if(Wallet.CoinERC20(MarketList[exchange_market].trade_wallet) == true){
					block_fee1 = Convert.ToDecimal(double_epsilon); // The minimum trade size for ERC20 tokens
				}
			}
			if(base_wallet_blockchaintype == 6){
				block_fee2 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[exchange_market].base_wallet));
				if(Wallet.CoinERC20(MarketList[exchange_market].base_wallet) == true){
					block_fee2 = Convert.ToDecimal(double_epsilon); // The minimum trade size for ERC20 tokens
				}
			}
			
			if(total < block_fee2 || amount < block_fee1){
				//The trade amount is too small
				code = 10;
				return;  				
			}
			
			//ERC20 only check
			bool sending_erc20 = false;
			decimal erc20_amount = 0;
			int erc20_wallet = 0;
			if(ord.type == 1 && Wallet.CoinERC20(MarketList[exchange_market].base_wallet) == true){
				//Maker is selling so we are buying trade with ERC20
				sending_erc20 = true;
				erc20_amount = total;
				erc20_wallet = MarketList[exchange_market].base_wallet;
			}else if(ord.type == 0 && Wallet.CoinERC20(MarketList[exchange_market].trade_wallet) == true){
				//Maker is buying so we are selling trade that is also an ERC20
				sending_erc20 = true;
				erc20_amount = amount;
				erc20_wallet = MarketList[exchange_market].trade_wallet;
			}
			
			if(sending_erc20 == true){
				//And now look at all your maker open orders on the exchange that send this token
				lock(MyOpenOrderList){
					for(int i = 0; i < MyOpenOrderList.Count; i++){
						if(MyOpenOrderList[i].type == 0 && MarketList[MyOpenOrderList[i].market].base_wallet == erc20_wallet){
							//We are sending this token in another order, add it to the erc20_amount
							erc20_amount += Math.Round(MyOpenOrderList[i].price*MyOpenOrderList[i].amount,8);
						}else if(MyOpenOrderList[i].type == 1 && MarketList[MyOpenOrderList[i].market].trade_wallet == erc20_wallet){
							erc20_amount += MyOpenOrderList[i].amount;
						}
					}
				}
				
				//Make sure the allowance is there already
				decimal allowance = GetERC20AtomicSwapAllowance(GetWalletAddress(erc20_wallet),ERC20_ATOMICSWAP_ADDRESS,erc20_wallet);
				if(allowance < 0){
					code = 11;
					return; 					
				}else if(allowance < erc20_amount){
					if(approve_erc20 == false){
						code = 12;
						return;
					}
					//Create a transaction with this permission to send up to this amount
					allowance = 1000000; //1 million tokens by default
					if(erc20_amount > allowance){allowance = erc20_amount;}
					CreateAndBroadcastERC20Approval(erc20_wallet,allowance,ERC20_ATOMICSWAP_ADDRESS);
					code = 13;				
					return; 										
				}
			}
			
        	//Because tokens are indivisible at the moment, amounts can only be in whole numbers
        	bool ntp1_wallet = IsWalletNTP1(MarketList[exchange_market].trade_wallet);
        	if(ntp1_wallet == true){
        		if(Math.Abs(Math.Round(amount)-amount) > 0){
        			code = 14;
					return;        			
        		}
        		amount = Math.Round(amount);
        	}
        	
        	//Cannot match order when another order is involved deeply in trade
        	bool too_soon = false;
        	lock(MyOpenOrderList){
				for(int i = 0;i < MyOpenOrderList.Count;i++){
        			if(MyOpenOrderList[i].order_stage > 0){ too_soon = true; break; } //Your maker order is matching something
        			if(MyOpenOrderList[i].is_request == true){ too_soon = true; break; } //Already have another taker order
				}
        	}
        	
        	if(too_soon == true){
    			code = 15;
				return;        		
        	}
			
			//Check to see if any other open orders of mine
			if(MyOpenOrderList.Count >= total_markets){
    			code = 16;
				return;				
			}
			
			//Everything is good, create the request now
			//This will be a match open order (different than a general order)
			OpenOrder taker_ord = new OpenOrder();
			taker_ord.is_request = true; //Match order
			taker_ord.order_nonce = ord.order_nonce;
			taker_ord.market = ord.market;
			taker_ord.type = 1 - ord.type; //Opposite of the original order type
			taker_ord.price = ord.price;
			taker_ord.amount = amount;
			taker_ord.original_amount = amount;
			taker_ord.order_stage = 0;
			taker_ord.my_order = true; //Very important, it defines how much the program can sign automatically
			
			bool worked = SubmitMyOrderRequest(taker_ord,true);
			
			if(worked == true){
				//Add to lists and close form
				if(MyOpenOrderList.Count > 0){
					//Close all the other open orders until this one is finished
					QueueAllOpenOrders();
				}
				
				lock(MyOpenOrderList){
					MyOpenOrderList.Add(taker_ord); //Add to our own personal list
				}
				ExchangeWindow.PendOrder(taker_ord.order_nonce);
				if(main_window_loaded == true){
					Application.Invoke(delegate
                    {
                        main_window.Open_Order_List_Public.NodeStore.AddNode(taker_ord);
                    });					
				}
				code = 1;
				return;
			}
			code = 19;
		}
		
		public static async void TraderAPIChangeMarket(string target_market)
		{
			int which_market = TraderAPI_Selected_Market(target_market);
			if(which_market < 0){return;}
			if(which_market == exchange_market){return;}
			
			int oldmarket = exchange_market;
			exchange_market = which_market;
			trader_api_changing_markets = true;
			
			if(main_window_loaded == true){
				main_window.Change_Market_Info_Only(target_market,true);
			}
			await Task.Run(() => {ExchangeWindow.ClearMarketData(oldmarket);GetCNMarketData(exchange_market);} );
			if(main_window_loaded == true){
				main_window.Change_Market_Info_Only(target_market,false);
			}
			trader_api_changing_markets = false;
		}
		
		public static int TraderAPI_Selected_Market(string mform)
		{
			for(int i=0;i < MarketList.Count;i++){
				if(mform == MarketList[i].trade_symbol+"/"+MarketList[i].base_symbol){
					return i;
				}
			}
			return -1;
		}

		public static JArray TraderAPIMyOpenOrders()
		{
			JArray open_orders = new JArray();
			// Get all my open orders
			lock(MyOpenOrderList){
				for(int i=0;i < MyOpenOrderList.Count;i++){
					JObject ord = new JObject();
					ord["orderID"] = MyOpenOrderList[i].order_nonce;
					ord["price"] = MyOpenOrderList[i].Format_Price;
					ord["amount"] = MyOpenOrderList[i].Format_Amount;
					ord["minAmount"] = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",MyOpenOrderList[i].minimum_amount);
					ord["filled"] = MyOpenOrderList[i].Format_Filled;
					ord["orderType"] = MyOpenOrderList[i].Format_Type;
					ord["market"] = MyOpenOrderList[i].Format_Market;
					if(MyOpenOrderList[i].order_stage > 0){
						ord["status"] = "Trade in Process";
					}else{
						ord["status"] = "Active";
					}
					if(MyOpenOrderList[i].is_request == true){
						ord["isMaker"] = false;
					}else{
						ord["isMaker"] = true;
					}
					open_orders.Add(ord);
				}
			}
			return open_orders;			
		}
		
		public static JArray TraderAPIRecentTrades()
		{
			JArray recent = new JArray();
			lock(RecentTradeList[exchange_market]){
				//Most recent trade list is arranged newest to oldest
				for(int i=0;i < RecentTradeList[exchange_market].Count;i++){
					JObject ob = new JObject();
					ob["tradeTime"] = RecentTradeList[exchange_market][i].utctime.ToString();
					if(RecentTradeList[exchange_market][i].type == 0){
						// Buy Trade = Sell Order
						ob["tradeType"] = "BUY";
					}else{
						ob["tradeType"] = "SELL";
					}
					ob["price"] = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",RecentTradeList[exchange_market][i].price);
					ob["amount"] = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",RecentTradeList[exchange_market][i].amount);
					recent.Add(ob);
				}
			}
			return recent;			
		}
		
		public static JArray TraderAPIAllOpenOrders(int order_type)
		{
			// Can return buy and sell orders only
			JArray sorted = new JArray();
			lock(OpenOrderList[exchange_market]){
				for(int i=0;i < OpenOrderList[exchange_market].Count;i++){
					// Go through all the orders and put them in order from highest price to lowest
					if(OpenOrderList[exchange_market][i].type == order_type){
						if(OpenOrderList[exchange_market][i].order_stage == 0){
							JObject ord = new JObject();
							ord["orderID"] = OpenOrderList[exchange_market][i].order_nonce;
							ord["price"] = OpenOrderList[exchange_market][i].Format_Price;
							ord["amount"] = OpenOrderList[exchange_market][i].Format_Amount;
							ord["minAmount"] = String.Format(CultureInfo.InvariantCulture,"{0:0.########}",OpenOrderList[exchange_market][i].minimum_amount);
							bool added = false;
							for(int i2=0;i2 < sorted.Count;i2++){
								if(Convert.ToDecimal(sorted[i2]["price"].ToString(),CultureInfo.InvariantCulture) < OpenOrderList[exchange_market][i].price){
									// Add the ord before this price
									added = true;
									sorted.Insert(i2,ord);
									break;
								}
							}
							if(added == false){
								sorted.Add(ord); // Add to end
							}
						}
					}
				}
			}
			return sorted;
		}
		
		public static string TraderAPICodeMessage(int code)
		{
			// Every int code has a message attached to it
			switch (code) {
				case 0:
					return "Failure";
				case 1:
					return "Success";
				case 2:
					return "Unable to change the market";
				case 3:
					return "Unable to cancel order, it's involved in trade";
				case 4:
					return "Unable to cancel all orders, one or more orders are involved in trade";
				case 5:
					return "Unable to find desired maker order";
				case 6:
					return "Amount cannot be less than the minimum match";
				case 7:
					return "Amount cannot be greater than the order";
				case 8:
					return "This order amount is too small. Must be at least twice the CN fee";
				case 9:
					return "Trade wallets balance too low or wallet currently unavailable";
				case 10:
					return "This trade amount is too small to match because it is lower than the blockchain fee";
				case 11:
					return "Error determining ERC20 token contract allowance, please try again";
				case 12:
					return "Permission is required from this token's contract to send this amount to the NebliDex atomic swap contract. Please give permission";
				case 13:
					return "Token approval transaction sent. Please wait for your approval to be confirmed by the Ethereum network then try again.";
				case 14:
					return "All NTP1 tokens are indivisible at this time. Must be whole amounts";
				case 15:
					return "Another order is currently involved in trade. Please wait and try again";
				case 16:
					return "You have exceed the maximum amount of open orders";
				case 17:
					return "Cannot cancel a taker order";
				case 18:
					return "Cannot match your own order";
				case 19:
					return "Critical Node rejected your order request";
				case 20:
					return "All NTP1 tokens are indivisible at this time. Must be whole minimum amounts";
				case 21:
					return "Cannot create order while markets are changing";
				case 22:
					return "Cannot query market data while markets are changing";
				case 23:
					return "This price is higher than the maximum price of 10 000 000";
				default:
					return "";
			}
		}
		
		public static async void StartHeadlessTraderAPI(object state)
		{
			//This code attempts to open the client as a trader api server
			trader_api_activated = false;
			await Task.Run(() => SetTraderAPIServer(true) );
			if(trader_api_activated == false){
				Console.WriteLine("Failed to activate the Trader API server.");
				NebliDexNetLog("Failed to activate the Trader API server.");
				Headless_Application_Close();
			}else{
				Console.WriteLine("Trader API server now active. Port is "+App.trader_api_port+".");
				Console.WriteLine("Running Trader API server...");
			}
		}
	}
	
}