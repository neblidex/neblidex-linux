using System;
using System.Threading;
using System.Data;
using Mono.Data.Sqlite;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Globalization;
using NBitcoin;
using System.Reflection;
using Gtk;

namespace NebliDex_Linux
{
	public partial class App
	{

		//Header files
		public static ExchangeWindow main_window = null;
		public static bool main_window_loaded = false;
        public static int default_ui_look = 0; //UI look when ExchangeWindow opens

		//Mainnet version
		public static int protocol_version = 7; //My protocol version
		public static int protocol_min_version = 7; //Minimum accepting protocol version
		public static string version_text = "v7.0.0";
		public static bool run_headless = false; //If true, this software is ran in critical node mode without GUI on startup
		public static bool http_open_network = true; //This becomes false if user closes window
		public static int sqldatabase_version = 3;
		public static int accountdat_version = 1; //The version of the account wallet

		//Lowest testnet version: 7
		//Lowest mainnet version: 7

		//Version 7
        //Added new markets (15!)
        //Added 2 new wallets for stablecoins USDC, DAI based on ERC20 standard     
        //NDEX/GRS, NDEX/MONA, NDEX/DAI, NDEX/USDC, NDEX/BCH, NDEX/ETH
        //NEBL/DAI, NEBL/USDC, LTC/DAI, LTC/USDC, BTC/DAI, BTC/USDC, BCH/DAI 
        //GRS/DAI, MONA/DAI

		public static string App_Path = AppDomain.CurrentDomain.BaseDirectory;

		public static bool critical_node = false; //Not critical node by default
		public static bool critical_node_pending = false; //This is for a node that is just connecting to the network (it cannot relay).
		public static int critical_node_port = 55364; //This is our critical node port < 65000
		public static int cn_ndex_minimum = 39000; //The amount required to become a critical node
		public static int cn_num_validating_tx = 0; //The amount of transactions being validated by the CN
		public static string my_external_ip = ""; //Cache our IP address

		public static string Default_DNS_SEED = "https://neblidex.xyz/seed"; //The default seed, returns IP list of CNs
		public static string DNS_SEED = Default_DNS_SEED;
		public static int DNS_SEED_TYPE = 0; //Http protocol, 1 = Direct IP
		public static int wlan_mode = 0; //0 = Internet, 1 = WLAN, 2 = Localhost (This is for CN IP addresses returned)
        
		public static int exchange_market = 2; //NDEX/NEBL
        public static int total_markets = 37;
        public static int total_scan_markets = total_markets; // This number will vary if we are updating the markets
        public static int total_cointypes = 7; 
		//The total amount of cointypes supported by NebliDex
       //Possible cointypes are:
       //0 - Neblio based (including tokens)
       //1 - Bitcoin based
       //2 - Litecoin based
       //3 - Groestlcoin based
       //4 - Bitcoin Cash (ABC) based
       //5 - Monacoin based
       //6 - Ethereum based

		public static Random app_random_gen = new Random(); //App random number generator
		public static string my_rsa_privkey, my_rsa_pubkey; //These are used to exchange a one time use password nonce between validator and TN
		public static string my_wallet_pass = ""; //Password used to load the wallet
		public static Timer PeriodicTimer; //Timer for balance and other things, ran every 5 seconds
		public static Timer ConsolidateTimer; //Ran every 6 hours
		public static Timer CandleTimer; //This is a timer ran every 15 minutes
		public static Timer HeadlessTimer; //Only used for headless mode
		public static int next_candle_time = 0; //This is the time in seconds of the next candle
		public static int candle_15m_interval = 0; //4 of these is 90 minutes (one 7 day candle)
		public static int max_transaction_wait = 60 * 60 * 3; //The maximum amount to wait (in seconds)for a transaction to confirm before deeming it failed
		public static double double_epsilon = 0.00000001;
		public static decimal max_order_price = 10000000; //Maximum price is 10,000,000 ratio
		public static FileStream lockfile;

		//Market Info
		//Will Make Market Info modular and connect to certain wallet types
		public static List<Market> MarketList = new List<Market>();
		public class Market
		{
			public int index; //0 The location index of the market
			public string base_symbol; //The symbol for the base coin
			public int base_wallet; //The wallet connected to the base coin
			public string trade_symbol;
			public int trade_wallet;
			public bool active = true;

			public Market(int i)
			{
				index = i;
				if (index == 0)
				{
					//NEBL/BTC
					base_symbol = "BTC";
					trade_symbol = "NEBL";
					base_wallet = 1; //BTC wallet
					trade_wallet = 0; //NEBL wallet
				}
				else if (index == 1)
				{
					//NEBL/LTC
					base_symbol = "LTC";
					trade_symbol = "NEBL";
					base_wallet = 2; //LTC wallet
					trade_wallet = 0; //NEBL wallet                 
				}
				else if (index == 2)
				{
					//NDEX/NEBL
					base_symbol = "NEBL";
					trade_symbol = "NDEX";
					base_wallet = 0; //NEBL wallet
					trade_wallet = 3; //NDEX wallet                 
				}
				else if (index == 3)
				{
					//NDEX/BTC
					base_symbol = "BTC";
					trade_symbol = "NDEX";
					base_wallet = 1; //BTC wallet
					trade_wallet = 3; //NDEX wallet                 
				}
				else if (index == 4)
				{
					//NDEX/LTC
					base_symbol = "LTC";
					trade_symbol = "NDEX";
					base_wallet = 2; //LTC wallet
					trade_wallet = 3; //NDEX wallet                 
				}
				else if (index == 5)
				{
					//TRIF/NEBL
					base_symbol = "NEBL";
					trade_symbol = "TRIF";
					base_wallet = 0; //NEBL wallet
					trade_wallet = 4; //TRIF wallet                 
				}
				else if (index == 6)
				{
					//QRT/NEBL
					base_symbol = "NEBL";
					trade_symbol = "QRT";
					base_wallet = 0; //NEBL wallet
					trade_wallet = 5; //QRT wallet

					active = false; //Deactivate this market
				}
				else if (index == 7)
				{
					//PTN/NEBL
					base_symbol = "NEBL";
					trade_symbol = "PTN";
					base_wallet = 0; //NEBL wallet
					trade_wallet = 6; //PTN wallet

					active = false;
				}else if (index == 8)
                {
                    //NAUTO/NEBL
                    base_symbol = "NEBL";
                    trade_symbol = "NAUTO";
                    base_wallet = 0; //NEBL wallet
                    trade_wallet = 7; //NAUTO wallet                    
                }
                else if (index == 9)
                {
                    //NCC/NEBL
                    base_symbol = "NEBL";
                    trade_symbol = "NCC";
                    base_wallet = 0; //NEBL wallet
                    trade_wallet = 8; //NCC wallet                  
                }
                else if (index == 10)
                {
                    //CHE/NEBL
                    base_symbol = "NEBL";
                    trade_symbol = "CHE";
                    base_wallet = 0; //NEBL wallet
                    trade_wallet = 9; //CHE wallet 
                    
					active = false;
                }
                else if (index == 11)
                {
                    //HODLR/NEBL
                    base_symbol = "NEBL";
                    trade_symbol = "HODLR";
                    base_wallet = 0; //NEBL wallet
                    trade_wallet = 10; //HODLR wallet                   
                }
                else if (index == 12)
                {
                    //NTD/NEBL
                    base_symbol = "NEBL";
                    trade_symbol = "NTD";
                    base_wallet = 0; //NEBL wallet
                    trade_wallet = 11; //NTD wallet                 
                }
				else if (index == 13)
                {
                    //TGL/NEBL
                    base_symbol = "NEBL";
                    trade_symbol = "TGL";
                    base_wallet = 0; //NEBL wallet
                    trade_wallet = 12; //TGL wallet                 
                }
				else if (index == 14)
                {
                    //TGL/NEBL
                    base_symbol = "NEBL";
                    trade_symbol = "IMBA";
                    base_wallet = 0; //NEBL wallet
                    trade_wallet = 13; //IMBA wallet                 
				}
				else if (index == 15)
                {
                    base_symbol = "BTC";
                    trade_symbol = "LTC";
                    base_wallet = 1; //BTC
                    trade_wallet = 2; //LTC
                }
                else if (index == 16)
                {
                    base_symbol = "BTC";
                    trade_symbol = "BCH";
                    base_wallet = 1; //BTC
                    trade_wallet = 15; //BCH
                }
                else if (index == 17)
                {
                    base_symbol = "BTC";
                    trade_symbol = "ETH";
                    base_wallet = 1; //BTC
                    trade_wallet = 17; //ETH
                }
                else if (index == 18)
                {
                    base_symbol = "BTC";
                    trade_symbol = "MONA";
                    base_wallet = 1;
                    trade_wallet = 16; //MONA
                }
                else if (index == 19)
                {
                    base_symbol = "NEBL";
                    trade_symbol = "GRS";
                    base_wallet = 0;
                    trade_wallet = 14; //GRS
                }
                else if (index == 20)
                {
                    base_symbol = "LTC";
                    trade_symbol = "ETH";
                    base_wallet = 2;
                    trade_wallet = 17; //ETH
                }
                else if (index == 21)
                {
                    base_symbol = "LTC";
                    trade_symbol = "MONA";
                    base_wallet = 2;
                    trade_wallet = 16; //MONA
				}
                else if (index == 22)
                {
                    base_symbol = "GRS";
                    trade_symbol = "NDEX";
                    base_wallet = 14;
                    trade_wallet = 3; //NDEX
                }
                else if (index == 23)
                {
                    base_symbol = "MONA";
                    trade_symbol = "NDEX";
                    base_wallet = 16;
                    trade_wallet = 3; //NDEX
                }
                else if (index == 24)
                {
                    base_symbol = "BCH";
                    trade_symbol = "NDEX";
                    base_wallet = 15;
                    trade_wallet = 3; //NDEX
                }
                else if (index == 25)
                {
                    base_symbol = "ETH";
                    trade_symbol = "NDEX";
                    base_wallet = 17;
                    trade_wallet = 3; //NDEX
                }
                else if (index == 26)
                {
                    base_symbol = "DAI";
                    trade_symbol = "NDEX";
                    base_wallet = 18; //DAI
                    trade_wallet = 3; //NDEX
                }
                else if (index == 27)
                {
                    base_symbol = "USDC";
                    trade_symbol = "NDEX";
                    base_wallet = 19; //USDC
                    trade_wallet = 3; //NDEX
                }
                else if (index == 28)
                {
                    base_symbol = "DAI";
                    trade_symbol = "NEBL";
                    base_wallet = 18;
                    trade_wallet = 0; //NEBL
                }
                else if (index == 29)
                {
                    base_symbol = "USDC";
                    trade_symbol = "NEBL";
                    base_wallet = 19;
                    trade_wallet = 0; //NEBL
                }
                else if (index == 30)
                {
                    base_symbol = "DAI";
                    trade_symbol = "LTC";
                    base_wallet = 18;
                    trade_wallet = 2; //LTC
                }
                else if (index == 31)
                {
                    base_symbol = "USDC";
                    trade_symbol = "LTC";
                    base_wallet = 19;
                    trade_wallet = 2; //LTC
                }
                else if (index == 32)
                {
                    base_symbol = "DAI";
                    trade_symbol = "BTC";
                    base_wallet = 18;
                    trade_wallet = 1; //BTC
                }
                else if (index == 33)
                {
                    base_symbol = "USDC";
                    trade_symbol = "BTC";
                    base_wallet = 19;
                    trade_wallet = 1; //BTC
                }
                else if (index == 34)
                {
                    base_symbol = "DAI";
                    trade_symbol = "BCH";
                    base_wallet = 18;
                    trade_wallet = 15; //BCH
                }
                else if (index == 35)
                {
                    base_symbol = "DAI";
                    trade_symbol = "GRS";
                    base_wallet = 18;
                    trade_wallet = 14; //GRS
                }
                else if (index == 36)
                {
                    base_symbol = "DAI";
                    trade_symbol = "MONA";
                    base_wallet = 18;
                    trade_wallet = 16; //MONA
                }
			}

			public string format_market
			{
				get
				{
					return trade_symbol + "/" + base_symbol;
				}
			}
		}

		//Wallet Info
		//Last pair for each wallet is used to trade
		//Cannot change address if open orders present
		public static List<Wallet> WalletList = new List<Wallet>();

		[Gtk.TreeNode(ListOnly = true)]
		public class Wallet : Gtk.TreeNode
		{
			public int type; 
			public string private_key;
			public string address;
			public decimal balance;
			public int status = 0; //0 - avail, 1 - pending, 2 - waiting
			public int blockchaintype = 0;

			public static int total_coin_num = 20; //Total number of possible different wallet coins

            public static bool CoinActive(int ctype)
            { //Coins that are not active anymore
                if (ctype == 5 || ctype == 6 || ctype == 9)
                { //QRT, CHE, PTN are not active anymore
                    return false;
                }
                else
                {
                    return true;
                }
            }

			public static bool CoinERC20(int ctype)
            {
                if (ctype == 18 || ctype == 19)
                { //New ETH based ERC20 tokens
                    return true;
                }
                return false;
            }

            public static bool CoinNTP1(int ctype)
            {
                //Returns true if the type is a NTP1 type
                if (ctype >= 3 && ctype <= 13)
                {
                    return true;
                }
                return false;
            }

            public static int WalletType(int btype)
            {
                //Returns type based on the blockchain type
                if (btype < 3)
                {
                    return btype;
                }
                else
                {
                    if (btype == 3)
                    {
                        //GRS Wallet
                        return 14;
                    }
                    else if (btype == 4)
                    {
                        //BCH
                        return 15;
                    }
                    else if (btype == 5)
                    {
                        //MONA
                        return 16;
                    }
                    else if (btype == 6)
                    {
                        //ETH
                        return 17;
                    }
                }
                return 0; //Otherwise, its neblio based
            }

            public static int BlockchainType(int type)
            {
                if (type == 0 || (type > 2 && type < 14))
                {
                    return 0; //Neblio based (including tokens
                }
                else if (type == 1)
                {
                    return 1;
                }
                else if (type == 2)
                {
                    return 2;
                }
                else if (type == 14)
                {
                    return 3;
                }
                else if (type == 15)
                {
                    return 4;
                }
                else if (type == 16)
                {
                    return 5;
                }
				else if (type == 17 || type == 18 || type == 19)
                {
                    return 6;
                }
                return 0;
            }

			[Gtk.TreeNodeValue(Column = 0)]
			public string Coin
			{
				get
				{
					if (type == 0)
                    {
                        return "NEBL";
                    }
					else if (type == 1)
					{
						return "BTC";
					}
					else if (type == 2)
					{
						return "LTC";
					}
					else if (type == 3)
					{ //Important wallet
						return "NDEX";
					}
					else if (type == 4)
					{
						return "TRIF"; //3rd party NTP1 token
					}
					else if (type == 5)
					{
						return "QRT"; //QRT 3rd party token
					}
					else if (type == 6)
					{
						return "PTN"; //PTN 3rd party token
					}else if (type == 7)
                    {
                        return "NAUTO"; //NAUTO 3rd party token
                    }
                    else if (type == 8)
                    {
                        return "NCC"; //NCC 3rd party token
                    }
                    else if (type == 9)
                    {
                        return "CHE"; //CHE 3rd party token
                    }
                    else if (type == 10)
                    {
                        return "HODLR"; //HODLR 3rd party token
                    }
                    else if (type == 11)
                    {
                        return "NTD"; //NTD 3rd party token
                    }
					else if (type == 12)
                    {
                        return "TGL"; //TGL 3rd party token
                    }
					else if (type == 13)
                    {
                        return "IMBA"; //IMBA 3rd party token
					}
					else if (type == 14)
                    {
                        return "GRS"; //GRS coin
                    }
                    else if (type == 15)
                    {
                        return "BCH"; //Bitcoin Cash
                    }
                    else if (type == 16)
                    {
                        return "MONA"; //Monacoin
                    }
                    else if (type == 17)
                    {
                        return "ETH"; //Ethereum
					}
					else if (type == 18)
                    {
                        return "DAI"; //DAI ERC20 stablecoin
                    }
                    else if (type == 19)
                    {
                        return "USDC"; //USDC ERC20 stablecoin
                    }
					return "";
				}
			}

			public string ERC20Contract
            {
                get
                {
                    if(blockchaintype != 6){ //Not ETH
                        return "";
                    }else if(type == 18){ //DAI Contract
                        if(testnet_mode == false){
                            return "0x89d24A6b4CcB1B6fAA2625fE562bDD9a23260359"; 
                        }else{
                            return "0xDE24730E12C76a269E99b8E7668A0b73102AfCa1"; //Using REP Rinkeby testnet tokens as DAI doesn't have any
                        }
                    }else if(type == 19){ //USDC Proxy Contract
                        //USDC has upgradeable contracts
                        if(testnet_mode == false){
                            return "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48";
                        }else{
                            return ""; //USDC doesn't have a testnet
                        }
                    }
                    return "";                  
                }
            }

            //The amount of decimal places for the token
            public decimal ERC20Decimals
            {
                get
                {
                    if(blockchaintype != 6){ //Not ETH
                        return 8;
                    }else if(type == 18){ //DAI Contract
                        return 18; //Dai contracts have 18 decimal places
                    }else if(type == 19){
                        return 6; //USDC has 6 decimal places
                    }
                    return 8;                   
                }
            }   

			public string TokenID
			{
				get
				{
					if (type == 0 || blockchaintype != 0)
                    { //Not Neblio token
                        return "";
					}
					else if (type == 3)
					{ //Important wallet
						if (testnet_mode == false)
						{
							return "LaAHPkQRtb9AFKkACMhEPR58STgCirv7RheEfk"; //NDEX
						}
						else
						{
							return "La7ma9nkcNTi7g4kQs4ewXHHD5fDRRXmespWMX"; //NDEX testnet token
						}
					}
					else if (type == 4)
					{
						if (testnet_mode == false)
						{
							return "La3QxvUgFwKz2jjQR2HSrwaKcRgotf4tGVkMJx"; //TRIF NTP1 token
						}
						else
						{
							return "La5rZ4dkUi6cnFiex8Hmts6zagLipP28CRWVhx"; //TRIF testnet token
						}
					}
					else if (type == 5)
					{
						if (testnet_mode == false)
						{
							return "La59cwCF5aF2HCMvqXok7Htn6fBE2kQnA96rrj"; //QRT NTP1 token
						}
						else
						{
							return "NoTestnetToken"; //QRT testnet token
						}
					}
					else if (type == 6)
					{
						if (testnet_mode == false)
						{
							return "La5NtFaP8EB6ozdqXWdWvzxuZuk3Q3VLic8sQJ"; //PTN NTP1 token
						}
						else
						{
							return "NoTestnetToken"; //PTN testnet token
						}
					}else if (type == 7)
                    {
                        if (testnet_mode == false)
                        {
                            return "La3DmJcJo162g54jj3rKunSkD7aw9Foj3y8CSK"; //NAUTO NTP1 token
                        }
                        else
                        {
                            return "NoTestnetToken"; //NAUTO testnet token
                        }
                    }
                    else if (type == 8)
                    {
                        if (testnet_mode == false)
                        {
                            return "La4sfZJmmfjoNbSjAy4868ftkPAqWrH97bVDE3"; //NCC NTP1 token
                        }
                        else
                        {
                            return "NoTestnetToken"; //NCC testnet token
                        }
                    }
                    else if (type == 9)
                    {
                        if (testnet_mode == false)
                        {
                            return "LaA7RwxDAzQjeYBGueqws25tNbJvTCyKYQ9pS4"; //CHE NTP1 token
                        }
                        else
                        {
                            return "NoTestnetToken"; //CHE testnet token
                        }
                    }
                    else if (type == 10)
                    {
                        if (testnet_mode == false)
                        {
                            return "La6ojSJKYiHMBBRCwnFt2Xn8acUDRmzYyjg9LL"; //HODLR NTP1 token
                        }
                        else
                        {
                            return "NoTestnetToken"; //HODLR testnet token
                        }
                    }
                    else if (type == 11)
                    {
                        if (testnet_mode == false)
                        {
                            return "La2r6UYDYR7YJTVZMK1hp8WQ5KRfgcCw6s5uZV"; //NTD NTP1 token
                        }
                        else
                        {
                            return "NoTestnetToken"; //NTD testnet token
                        }
					}else if (type == 12)
                    {
                        if (testnet_mode == false)
                        {
                            return "La8Ntf8zGgYXVtpzVZKxtRptyyP1jwm2RshumQ"; //TGL NTP1 token
                        }
                        else
                        {
                            return "LaAFsP9LkJsfBGRaqcscUQz6evueMyPumqDw5d"; //TGL testnet token
                        }
					}else if (type == 13)
                    {
                        if (testnet_mode == false)
                        {
							return "La6H1AekHNgh8jKcQirh12cQ23wTSMX9Th84a2"; //IMBA NTP1 token
                        }
                        else
                        {
							return "NoTestnetToken"; //IMBA testnet token
                        }
                    }
					return "";
				}
			}

			[Gtk.TreeNodeValue(Column = 1)]
			public string Amount
			{
				get { return String.Format(CultureInfo.InvariantCulture, "{0:0.########}", balance); }
			}

			[Gtk.TreeNodeValue(Column = 2)]
			public string S_Status
			{
				get
				{
					if (status == 0)
					{
						return "Avail";
					}
					else if (status == 1)
					{
						return "Pend";
					}
					else if (status == 2)
					{
						return "Wait";
					}
					return "";
				}

			}
		}

		public static List<CancelOrderToken> CancelOrderTokenList = new List<App.CancelOrderToken>();

		public class CancelOrderToken
		{
			//This is for situations where the cancel request arrives before the open order does
			//Otherwise order is removed without cancel token
			public string order_nonce; //The string associated with the Order
			public int arrivetime; //These tokens delete after 5 minutes
		}

		public static List<OpenOrder>[] OpenOrderList = new List<OpenOrder>[total_markets]; //Create array of type list

		[Gtk.TreeNode(ListOnly = true)]
		public class OpenOrder : Gtk.TreeNode
		{
			public string order_nonce; //Not really a hash, but a one-time code used to define order
            public string[] ip_address_port = new string[2]; //Holds both port and IP address (Only CNs have this)
            public string cn_relayer_ip = ""; //This is the CN that first received the order 
            public int market;
            public int type; //0 or 1 (Buy or Sell)
            public decimal price; //Decimal value representing price
            public bool is_request = false; //This will show if this is a market order (taker order)
            public decimal amount; //Amount currently available / requesting
            public decimal original_amount; //Amount started available / requested (Only used for my orders)
            public decimal minimum_amount; //This is a user-set minimum order size
            public bool my_order = false; //True if this is my order
            public int order_stage;
            //0 - available to trade (visible)
            //Maker Information
            //1 - maker order is hidden from view (pended by CN)
            //2 - maker accepted trade request
            //3 - maker received taker information
            // In stage 3, maker will wait until taker contract has correct balance before broadcasting maker contract
            //4 - maker has tx sent to validator to broadcast 
            // In stage 4, maker cannot close program now as may miss time when taker pulls from maker contract
            // Maker contract is continuously monitored for spending transaction
            // Once taker has funded contract, maker extracts secret and pulls from taker contract immediately
            // After successful pull, maker is available to trade again

            //Taker Information
            //1 - Maker has accepted taker request
            //2 - Taker sent contract information and tx to validator
            // In this stage, taker may receive notice of cancelation from validator, this closes request but taker will
            // continue to monitor contract address for balance after refund time just in case
            //3 - Taker received maker information
            // In this stage, taker waits for maker contract to fund, once funded, pulls entire balance then closes request
            //4 - Request is canceled by validator

            public int pendtime; //The time when the order is pended
            public uint cooldownend = 0; //The time when the order is available for trading again
            public bool deletequeue = false; //This is for CN deletes only
            public bool validating = false; //This will be true if this person is choosing the validator
            public bool queued_order = false; //Queued orders will try to repost every 30 seconds
                                              //Fees are determined based on candles from 7 day charts: average of (N-2 & N-3)
                                              //If not enough data, defaults to 10

			public OpenOrder()
			{
				ip_address_port[0] = "";
				ip_address_port[1] = "";
			}

			//Properties for the UI
			[Gtk.TreeNodeValue(Column = 2)]
			public string Format_Price
			{
				get {
					if (filled_node == false) { return ""; }
					return String.Format(CultureInfo.InvariantCulture, "{0:0.########}", price); 
				}
			}

			[Gtk.TreeNodeValue(Column = 3)]
			public string Format_Amount
			{
				get { 
					if (filled_node == false) { return ""; }
					return String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount); 
				}
			}

			[Gtk.TreeNodeValue(Column = 10)]
			public string Format_Original_Amount
			{
				get { return String.Format(CultureInfo.InvariantCulture, "{0:0.########}", original_amount); }
			}

			[Gtk.TreeNodeValue(Column = 0)]
			public string Format_Market
			{
				get
				{
					string mark_string = MarketList[market].trade_symbol + "/" + MarketList[market].base_symbol;
					return mark_string;
				}
			}

			[Gtk.TreeNodeValue(Column = 1)]
			public string Format_Type
			{
				get
				{
					if (type == 0)
                    {
                        if (queued_order == true) { return "QUEUED BUY"; }
                        if (is_request == false) { return "BUY"; } else { return "MARKET BUY"; }
                    }
                    else
                    {
                        if (queued_order == true) { return "QUEUED SELL"; }
                        if (is_request == false) { return "SELL"; } else { return "MARKET SELL"; }
                    }
				}
			}

			[Gtk.TreeNodeValue(Column = 4)]
			public string Format_Filled
			{
				get
				{
					if (is_request == false)
					{
						return Convert.ToString(Math.Round((1m - amount / original_amount) * 100m, 2)) + "%";
					}
					else
					{
						return "Processing";
					}
				}
			}

			[Gtk.TreeNodeValue(Column = 5)]
			public string Format_Total
			{
				get { 
					if (filled_node == false) { return ""; }
					return String.Format(CultureInfo.InvariantCulture, "{0:0.########}", Math.Round(amount * price, 8)); 
				}
			}

			[Gtk.TreeNodeValue(Column = 6)]
			public bool cancel_selected = false;
            
			[Gtk.TreeNodeValue(Column = 7)]
            public bool cancel_visible //If we can see the cancel checkbox or not
            {
                get
                {
					if (is_request == false){
						return true;
					}else{
						return false;
					}
                }
            }

			[Gtk.TreeNodeValue(Column = 8)]
			public bool filled_node = true; //Visible or not visible on chart

			[Gtk.TreeNodeValue(Column = 9)]
            public int row_height = 23; //Default row height
            
		}

		//Simple class for MyTrades
		[Gtk.TreeNode(ListOnly = true)]
		public class MyTrade : Gtk.TreeNode
		{
			[Gtk.TreeNodeValue(Column = 0)]
			public string Date;
			[Gtk.TreeNodeValue(Column = 1)]
			public string Pair;
			[Gtk.TreeNodeValue(Column = 2)]
			public string Type;
			[Gtk.TreeNodeValue(Column = 3)]
			public string Price;
			[Gtk.TreeNodeValue(Column = 4)]
			public string Amount;
			public string TxID;
		}

		//Simple class for CN Fees
        [Gtk.TreeNode(ListOnly = true)]
        public class MyCNFee : Gtk.TreeNode
        {
            [Gtk.TreeNodeValue(Column = 0)]
            public string Date;
            [Gtk.TreeNodeValue(Column = 1)]
            public string Pair;
            [Gtk.TreeNodeValue(Column = 2)]
            public string Fee;
        }

		//Only 24 hours are stored in recent trade list
		public static List<RecentTrade>[] RecentTradeList = new List<RecentTrade>[total_markets];

		[Gtk.TreeNode(ListOnly = true)]
		public class RecentTrade : Gtk.TreeNode
		{
			public int utctime;
			public int market;
			public int type;
			public decimal price;
			public decimal amount;

			//UI Properties

			[Gtk.TreeNodeValue(Column = 2)]
			public string Format_Price
			{
				get { return String.Format(CultureInfo.InvariantCulture, "{0:0.########}", price); }
			}

			[Gtk.TreeNodeValue(Column = 3)]
			public string Format_Amount
			{
				get { return String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount); }
			}

			[Gtk.TreeNodeValue(Column = 0)]
			public string Format_Time
			{
				get { return UTC2DateTime(utctime).ToString("HH: mm: ss"); }
			}

			[Gtk.TreeNodeValue(Column = 1)]
			public string Format_Type
			{
				get
				{
					if (type == 0)
					{
						return "BUY";
					}
					else
					{
						return "SELL";
					}
				}
			}
		}

		public static List<OpenOrder> MyOpenOrderList = new List<OpenOrder>(); //One list for my open orders

		//Dictionary to CN Nodes
		public static Dictionary<string, CriticalNode> CN_Nodes_By_IP = new Dictionary<string, CriticalNode>(); //IP refers to Neblio Address
		public class CriticalNode
		{
			public string ip_add; //Only field populated by all nodes
			public string signature_ip = null; //Used to verify IP address
			public decimal ndex; //Used to get validation node, amount of ndex
			public string pubkey; //Used to verify
			public int lastchecked; //The last time the node was checked for balance (checks at most every 15 minutes)
			public uint strikes; //In case the critical node acts up, 10 strikes and blacklisted for 10 days
			public bool rebroadcast = false; //Flag that is true when we are rebroadcasting the node
		}
		//Critical nodes only add other critical nodes after verified signed message from address with account minimum threshold of NDEX

		//Store the TNs that connect to the critical nodes by IP, we want to prevent 1 IP from having too many connections
		public static Dictionary<string, int> TN_Connections = new Dictionary<string, int>();

		//These classes are not being used
		public static List<CoolDownTrader> CoolDownList = new List<CoolDownTrader>(); //List for all cooldown traders (for CNs only)

		public class CoolDownTrader
		{
			public int utctime; //In seconds
			public int cointype; //0 = nebl blockchain, 1 = btc blockchain, 2 = ltc blockchain
			public string address; //The address of the recent requester
		}

		public static List<OrderRequest> OrderRequestList = new List<OrderRequest>(); //List of all order request (for CNs only)

		public class OrderRequest
		{
			public string order_nonce_ref; //A reference to the corresponding open order (May not exist anymore, thus close order request)
			public string request_id = ""; //A unique ID for this request
			public int market;
			public int utctime; //Order requests are deleted after 1 hr (should not be open so long anyway)
			public int type; //0 - buying, 1 - selling
			public int order_stage;
            //0 - just started
            //1 - accepted by maker
            //2 - validation started
            //3 - closed
            //Maker can only cancel order request up until 2 (when sent amount from taker wallet)
			public decimal ndex_fee; //The fee required for the trade
			public string validator_pubkey = ""; //The pubkey to the validator CN (not added initially - All CNs will know though)

			public string from_add_1; //Taker's Address Send
			public string to_add_1 = ""; //Maker's Address Receive
			public decimal amount_1; //Amount taker is sending maker

			public string from_add_2 = ""; //Maker's Address Send
			public string to_add_2; //Taker's Address Receive
			public decimal amount_2; //Amount maker is sending taker
			public string custodial_add = ""; //The middle man account on the validation Node
			public int who_validate = 0; //0 = Maker, 1 = Taker

			public string[] ip_address_maker = new string[2]; //Contains both the port and IP of maker
			public string[] ip_address_taker = new string[2]; //Same but for taker
			public string maker_cn_ip = ""; //The order creator's CN
			public string taker_cn_ip = ""; //The order matcher CN
		}

		public static List<LastPriceObject>[] ChartLastPrice = new List<LastPriceObject>[2]; //Two Lists, 15 minutes and 90 minutes worth for each market
		public static int ChartLastPrice15StartTime = 0; //The time the list started collecting data

		public class LastPriceObject
		{
			public int market;
			public decimal price;
			public int atime = 0; //The time the trade was completed
		}

		//Candles are made after 15 minutes and 90 minutes respectively and stored in SQLite DB
		public static List<Candle> VisibleCandles = new List<Candle>(); //The candles visible on screen
																		//Loaded from the database and shown on screen, there is a maximum of 100 visible candles

		public class Candle
		{
			public double high;
			public double low;
			public double open;
			public double close;

			//public System.Windows.Shapes.Rectangle rect;
			//public System.Windows.Shapes.Line line;

			public Candle() { }
			public Candle(double op)
			{
				high = op; open = op; close = op; low = op;
			}
		}

		//Start of Main program
		public static void Main(string[] args)
		{

			if (args.Length > 0)
            {
                run_headless = true;
                //Interpret this as an attempt to be headless
                if (args[0] == "--criticalnode")
                {
					Console.WriteLine("Path: " + App_Path);
                    Start(null);
					//There needs to be a loop here to prevent the program from closing
					Headless_Infinite_Loop();
				}
                else
                {
                    Console.WriteLine("This application can only be ran headless with --criticalnode argument");
					Headless_Application_Close();
                }

            }
            else
            {
				//Clear chance of loading any preset themes
				Environment.SetEnvironmentVariable("GTK2_RC_FILES", " ");
                //Load the intro window
				Application.Init();
                IntroWindow win = new IntroWindow();
                win.Show();
				Application.Run();
            }
		}

		//This piece of code is ran at the beginning of the application, normally it starts the Intro window

		//Application is about to close
		public static void Headless_Application_Close()
		{
			if (lockfile != null)
			{
				lockfile.Unlock(0, 0); //Unlock the file
				lockfile.Close();
			}
			if (run_headless == true)
			{
				Console.WriteLine("Press any key to exit the program...");
				Console.ReadKey();
			}
			Environment.Exit(0);
		}

        public static void Headless_Infinite_Loop()
		{
			//This loop keeps the program open
			while(true)
			{
				Thread.Sleep(1000); //Just keep looping forever
			}
		}

		//Global exception handler, write to file
		public static void SetupExceptionHandlers()
		{
#if !DEBUG
        NebliDexNetLog("Loading release mode exception handlers");
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            LastExceptionHandler((Exception)e.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");


        TaskScheduler.UnobservedTaskException += (s, e) =>
            LastExceptionHandler(e.Exception, "TaskScheduler.UnobservedTaskException");

        GLib.ExceptionManager.UnhandledException += (arg) =>
            LastExceptionHandler((Exception)arg.ExceptionObject, "GLib.ExceptionManager.UnhandledException");
#endif
		}
            
        public static void LastExceptionHandler(Exception e, string source)
        {
            //Only for release mode, otherwise we want it to crash
			if(IgnorableException(e) == true){
				NebliDexNetLog("A non-fatal uncaught error has occurred: " + source + ": " + e);
				return;
			}
            NebliDexNetLog("A fatal unhandled error has occurred");
            if (e != null)
            {
                NebliDexNetLog(source + ": " + e); //Write the exception to file
            }
            main_window_loaded = false;
			if (lockfile != null)
            {
				try{
					lockfile.Unlock(0, 0);
				}catch(Exception){ }
                lockfile.Close();
				lockfile = null;
            }
            if (run_headless == false)
            {
				Application.Invoke(delegate
                {
					MessageBox(null, "Notice", "This program encountered a fatal error and will close", "OK");
					Application.Quit();
                });
				http_open_network = false;
            }
            else
            {
                Console.WriteLine("This program encountered a fatal error and will close");
                Headless_Application_Close();
                return;
            }
        }

        public static bool IgnorableException(Exception e)
		{
			//This will return true if the exception is ignorable thus return to program
			string etext = e.ToString();
			if(etext.IndexOf("MyGetResponseAsync",StringComparison.InvariantCulture) > -1){
                //This error will propagate due to bug in Mono
				return true;
			}
			if (etext.IndexOf("MobileAuthenticatedStream", StringComparison.InvariantCulture) > -1)
            {
                //This error will propagate due to bug in newer versions for Mono
                return true;
            }
			return false;
		}

        //All methods here are static (1 application)
        public static async void Start(IntroWindow i)
        {         
            //Create the Market Lists
            for (int it = 0; it < total_markets; it++)
            {
                //These market based lists make it easier to index orders
                OpenOrderList[it] = new List<OpenOrder>();
                //Create the Recent Trade Lists
                RecentTradeList[it] = new List<RecentTrade>();
            }

			//Add market list, the order is very important
            for (int im = 0; im < total_markets; im++)
            {
                Market mark = new Market(im);
                MarketList.Add(mark);
            }

            //Create the chart plotter
            ChartLastPrice[0] = new List<LastPriceObject>(); //24 hour
            ChartLastPrice[1] = new List<LastPriceObject>(); //7 Day

			//Set the default fees
            //Neblio rounds up to closest 0.0001
            blockchain_fee[0] = 0.00011m;  //This is fee per 1000 bytes (2000 hex characters)
            blockchain_fee[1] = 0.0012m; //Default fees per kb
            blockchain_fee[2] = 0.0020m;
            blockchain_fee[3] = 0.0020m;
            blockchain_fee[4] = 0.00002m; //BCH (2000 sat/kb default)
            blockchain_fee[5] = 0.0020m; //MONA
            blockchain_fee[6] = 5; //ETH default gas price in gwei

            //Set the dust minimums as well
            dust_minimum[0] = 0.0001m; //Cannot send an output less than this
            dust_minimum[1] = 0.0000547m;
            dust_minimum[2] = 0.0000547m;
            dust_minimum[3] = 0.0000547m;
            dust_minimum[4] = 0.0000001m; //BCH (Very low dust minimum)
            dust_minimum[5] = 0.001m; //MONA
            dust_minimum[6] = 0.000000001m; //ETH

            if (testnet_mode == true)
            {
                critical_node_port--; //Testnet is one below mainnet
            }

            if (run_headless == true)
            {
                Console.WriteLine("Loading NebliDex Program, version: " + version_text);
            }

            //Now create database
            if (i != null)
            {
				Application.Invoke(delegate
				{
					i.Intro_Status.Text = "Loading Databases";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
				});
            }
			bool ok = await Task.Run(() => CreateDatabase());
			if (ok == false) { return; } //This only occurs if lock file was already open
            
            //Load the wallet
            if (i != null)
            {
				Application.Invoke(delegate
                {
					i.Intro_Status.Text = "Loading Wallet";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
                });
            }
            else if (run_headless == true)
            {
                NebliDexNetLog("Headless mode initialized");
                Console.WriteLine("Loading Wallet");
            }
            NebliDexNetLog("Loading the wallet");
            CheckWallet(i); //Ran inline
            ok = await Task.Run(() => LoadWallet());
			if (ok == false) { return; } //Failed to load wallet

            //Create the RSA keys
            NebliDexNetLog("Creating RSA Keys");
            string[] rsakeys = GenerateRSAKeys(1024);
            my_rsa_pubkey = rsakeys[0]; //Public key
            my_rsa_privkey = rsakeys[1]; //Private key

            //Connecting to Electrum
            if (i != null)
            {
				Application.Invoke(delegate
                {
					i.Intro_Status.Text = "Finding Electrum Servers";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
                });
            }
            else if (run_headless == true)
            {
                Console.WriteLine("Finding Electrum Servers");
            }

            NebliDexNetLog("Finding Electrum Servers");

            await Task.Run(() => FindElectrumServers());
         
            //Get the correct DNS Seed
            if (i != null)
            {
				Application.Invoke(delegate
                {
					i.Intro_Status.Text = "Finding Critical Nodes";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
                });
            }
            else if (run_headless == true)
            {
                Console.WriteLine("Finding Critical Nodes");
            }

            NebliDexNetLog("Finding Critical Nodes");
            if (File.Exists(App_Path + "/data/cn_list.dat") == false)
            {
                if (run_headless == false)
                {
					ManualResetEvent waiter = new ManualResetEvent(false);
					Application.Invoke(delegate
                    {
						SeedListWindow dns_seed = new SeedListWindow(DNS_SEED); //Window
                        dns_seed.Parent = i;
                        dns_seed.Modal = true;
						dns_seed.waiting = waiter; //We want to wait on the window to close
                        dns_seed.Show();
                    });
					waiter.WaitOne();
				}
                else
                {
                    //Just use the default seed
                    FindCNServers(false);
                }
            }
      
            NebliDexNetLog("Connecting Critical Node Server");
            if (i != null)
            {
				Application.Invoke(delegate
                {
					i.Intro_Status.Text = "Connecting Critical Node Server";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
                });
                await Task.Run(() => ConnectCNServer(true));
            }
            else if (run_headless == true)
            {
                Console.WriteLine("Connecting Critical Node Server");
                await Task.Run(() => ConnectCNServer(false));
            }         

            bool cncount_ok = await Task.Run(() => AccurateCNOnlineCount());
            if (cncount_ok == false)
            {
                //We need to update the list of connected nodes
                if (i != null)
                {
					Application.Invoke(delegate
                    {
						i.Intro_Status.Text = "Updating Critical Nodes List";
                        i.Intro_Status.Xalign = 0.5f;
                        i.Intro_Status.QueueDraw();
                    });
                }
                await Task.Run(() => FindCNServers(false));
                if (File.Exists(App_Path + "/data/cn_list.dat") == true)
                {
                    LoadCNList();
                }
            }
         
            if (i != null)
            {
				Application.Invoke(delegate
                {
					i.Intro_Status.Text = "Connecting Electrum Servers";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
                });
            }
            else if (run_headless == true)
            {
                Console.WriteLine("Connecting Electrum Servers");
            }         

            NebliDexNetLog("Connecting Electrum Servers");
            await Task.Run(() => ConnectElectrumServers(-1));

            if (i != null)
            {
				Application.Invoke(delegate
                {
					i.Intro_Status.Text = "Syncing Electrum Servers";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
                });
            }
            else if (run_headless == true)
            {
                Console.WriteLine("Syncing Electrum Servers");
            }

            NebliDexNetLog("Syncing Electrum Servers");
            await Task.Run(() => CheckElectrumServerSync());

            if (i != null)
            {
				Application.Invoke(delegate
                {
					i.Intro_Status.Text = "Retrieving Chart Data";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
                });
            }
            else if (run_headless == true)
            {
                Console.WriteLine("Retrieving Chart Data");
            }

			//Load the UI look
            if (run_headless == false)
            {
                ExchangeWindow.Load_UI_Config(); //Loads the look and the market
            }

            await Task.Run(() => GetCNMarketData(exchange_market)); //This will get the market data from the critical node

            if (run_headless == false)
            {
				Application.Invoke(delegate
                {
					main_window = new ExchangeWindow();
                    i.Intro_Status.Text = "Loading Charts";
                    i.Intro_Status.Xalign = 0.5f;
                    i.Intro_Status.QueueDraw();
                    main_window.LoadUI();
                    main_window.Show();
                    i.Destroy(); //Delete this window
					main_window.LegalWarning();
                    bool savedord = CheckSavedOrders();
                    if (savedord == true)
                    {
                        main_window.Prompt_Load_Saved_Orders();
                    }
                });
            }

            //Setup the timers that are run every 5 seconds
            //Mostly for Keep Alive and balance checking
            PeriodicTimer = new Timer(new TimerCallback(PeriodicNetworkQuery), null, 0, 5000);
            //Link timer to updating charts
            int waittime = next_candle_time - UTCTime();
            if (waittime < 0)
            {
                waittime = 0;
            } //Sync candle times across clients
            
			CandleTimer = new Timer(new TimerCallback(ExchangeWindow.PeriodicCandleMaker), null, waittime * 1000, System.Threading.Timeout.Infinite);

            //Ran every 6 hours and consolidates UTXOs, starts 30 seconds in after program load
            ConsolidateTimer = new Timer(new TimerCallback(WalletConsolidationCheck), null, 30 * 1000, 60000 * 60 * 6);

            //If headless, we will try to run headless in 20 seconds
            if (run_headless == true)
            {
                Console.WriteLine("Attempting to run as Critical Node in 20 seconds...");
                NebliDexNetLog("Attempting to run as Critical Node in 20 seconds");
                HeadlessTimer = new Timer(new TimerCallback(StartHeadlessCN), null, 20 * 1000, System.Threading.Timeout.Infinite);
            }

        }

        public static void CloseCNStatusWindow(IntroWindow status)
        {
            if (status == null) { return; }
            //This will be ran asynchronously on GUI thread
			Application.Invoke(delegate
			{
				status.Intro_Closed(null, null); //Release whatever wait
				status.Destroy();
			});
        }

        public static void UpdateCNStatusWindow(IntroWindow status, string msg)
        {
			Application.Invoke(delegate
            {
				if (status != null)
                {
                    status.Intro_Status.Text = msg;
					status.Intro_Status.Xalign = 0.5f;
					status.Intro_Status.QueueDraw();
                }
                else
                {
                    Console.WriteLine(msg);
                }
            });
        }

		public static bool CheckMonoVersion()
        {
            //Returns true if mono version is ok
            //This function will check the client to make sure they are running the correct version
            //If it is less, will show a message for the user to upgrade
            Type type = Type.GetType("Mono.Runtime");
            if (type != null)
            {
                MethodInfo dname = type.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
                if (dname != null)
                {
                    string version = dname.Invoke(null, null).ToString();
                    NebliDexNetLog("Mono Version: " + version);
                    int pos = version.IndexOf(".", StringComparison.InvariantCulture); //Find the first .
                    if (pos < 0) { return false; } //Can't find mono version
                    int majorv = Convert.ToInt32(version.Substring(0, pos));
                    int pos2 = version.IndexOf(".", pos + 1, StringComparison.InvariantCulture);
                    if (pos2 < 0) { return false; }
                    int minorv = Convert.ToInt32(version.Substring(pos + 1, pos2 - pos - 1));
                    //App was created in mono 5.12
					if (majorv < 5) { return false; }
                    if (majorv == 5)
                    {
                        if (minorv < 12) { return false; }
                    }
                    return true;
                }
            }
            return false;
        }

        public static bool CreateDatabase()
        {
			//This function creates the databases and loads the user data
			//Anything that gets deleted when user closes program is not stored in database
            
            //First check if data folder exists
            if (Directory.Exists(App_Path + "/data") == false)
            {
                //Folder does not exist, create the path
                Directory.CreateDirectory(App_Path + "/data");
            }
            
            //Try to acquire a file lock to prevent other instances to open while NebliDex is running
            try
            {
                lockfile = new FileStream(App_Path + "/data/lock.file", FileMode.OpenOrCreate);
				//Because the above doesn't work on Linux, must also use below
				lockfile.Lock(0, 0);
			}
            catch (Exception)
            {
				//Ran sync
				lockfile = null; //Couldn't acquire the lock
				if (run_headless == false)
                {
					Application.Invoke(delegate
                    {
						MessageBox(null, "Notice", "An instance of NebliDex is already open", "OK");
                        Application.Quit();
                    });
					return false;
                }
                else
                {
                    Console.WriteLine("An instance of NebliDex is already open");
					Headless_Application_Close();
                }
            }

            if (File.Exists(App_Path + "/data/debug.log") == true)
            {
                long filelength = new System.IO.FileInfo(App_Path + "/data/debug.log").Length;
                if (filelength > 10000000)
                { //Debug log is greater than 10MB
                    lock (debugfileLock)
                    {
                        File.Delete(App_Path + "/data/debug.log"); //Clear the old log
                    }
                }
            }

            NebliDexNetLog("Loading new instance of NebliDex version: " + version_text);
            if (testnet_mode == true)
            {
                NebliDexNetLog("Testnet mode is on");
            }

            SetupExceptionHandlers();

			//Check the Mono version (only for Linux version)
            bool mono_ok = CheckMonoVersion();
            if (mono_ok == false)
            {
                NebliDexNetLog("Mono version is out of date.");
                if (run_headless == true)
                {
                    Console.WriteLine("The mono runtime version is out of date. Please update to latest version from mono-project.com");
                    Headless_Application_Close();
                }
                else
                {
                    Application.Invoke(delegate
                    {
                        MessageBox(null, "Notice", "The mono runtime version is out of date. Please update to latest version from mono-project.com", "OK");
                        Application.Quit();
                    });
					return false;
                }
            }

			if (File.Exists(App_Path + "/data/neblidex.db") == false)
            {
                NebliDexNetLog("Creating databases");
                SqliteConnection.CreateFile(App_Path + "/data/neblidex.db");
                //Now create the tables
                string myquery;
                SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
                mycon.Open();

                //Create My Tradehistory table
                myquery = "Create Table MYTRADEHISTORY";
                myquery += " (nindex Integer Primary Key ASC, utctime Integer, market Integer, type Integer, price Text, amount Text, txhash Text, pending Integer)";
                SqliteCommand statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Create Transaction table
                myquery = "Create Table MYTRANSACTIONS";
                myquery += " (nindex Integer Primary Key ASC, utctime Integer, txhash Text, from_add Text, to_add Text, cointype Integer, amount Text,";
                myquery += " custodial_redeemscript_add Text,  custodial_redeemscript Text, counterparty_cointype Integer, type Integer, waittime Integer,";
                myquery += " order_nonce_ref Text, req_utctime_ref Integer, validating_nodes Text, makertxhash Text, atomic_unlock_time Integer, atomic_refund_time Integer,";
                myquery += " receive_amount Text, to_add_redeemscript Text, atomic_secret_hash Text, atomic_secret Text)";
                //Validating nodes are divided by | separator, first one is main validator, the others are auditors
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Create Validating Transaction table
                myquery = "Create Table VALIDATING_TRANSACTIONS";
                myquery += " (nindex Integer Primary Key ASC, utctime Integer, order_nonce_ref Text, maker_pubkey Text, taker_pubkey Text, custodial_privkey Text, maker_from_add Text,";
                myquery += " maker_feetx Text, maker_feetx_hash Text, maker_tx Text, maker_txhash Text, status Integer, redeemscript Text, ndex_fee Text, rbalance Text, claimed Integer,";
                myquery += " taker_feetx Text, taker_feetx_hash Text, validating_cn_pubkey Text, market Integer, reqtype Integer, redeemscript_add Text, waittime Integer,";
                myquery += " maker_sendamount Text, taker_receive_add Text, taker_tx Text)";
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Mostly for CNs to block known bad actors
                //Type can be 0 for IPs and 1 for Neblio addresses
                myquery = "Create Table BLACKLIST";
                myquery += " (nindex Integer Primary Key ASC, utctime Integer, type Integer, value Text)";
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Create Candlestick table (24 hour)
                myquery = "Create Table CANDLESTICKS24H";
                myquery += " (nindex Integer Primary Key ASC, utctime Integer, market Integer, highprice Text, lowprice Text, open Text, close Text)";
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Create Candlestick table (7 day)
                myquery = "Create Table CANDLESTICKS7D";
                myquery += " (nindex Integer Primary Key ASC, utctime Integer, market Integer, highprice Text, lowprice Text, open Text, close Text)";
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Create CN Transaction fee data table
                myquery = "Create Table CNFEES";
                myquery += " (nindex Integer Primary Key ASC, utctime Integer, market Integer, fee Text)";
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Create a table for version control
                myquery = "Create Table FILEVERSION (nindex Integer Primary Key ASC, version Integer)";
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();
                //Insert a row with version
                myquery = "Insert Into FILEVERSION (version) Values (" + sqldatabase_version + ");";
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Create Saved Orders data 
                myquery = "Create Table SAVEDORDERS";
                myquery += " (nindex Integer Primary Key ASC, market Integer, type Integer, nonce Text, price Text, amount Text, min_amount Text)";
                statement = new SqliteCommand(myquery, mycon);
                statement.ExecuteNonQuery();
                statement.Dispose();

                mycon.Dispose();
            }

            string myquery2;
            SqliteConnection mycon2 = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon2.Open();

            //Delete the Candles Database as they have come out of sync and obtain new chart from another server
            myquery2 = "Delete From CANDLESTICKS7D";
            SqliteCommand statement2 = new SqliteCommand(myquery2, mycon2);
            statement2.ExecuteNonQuery();
            statement2.Dispose();

            myquery2 = "Delete From CANDLESTICKS24H";
            statement2 = new SqliteCommand(myquery2, mycon2);
            statement2.ExecuteNonQuery();
            statement2.Dispose();

            //Additional params in case of older versions
            UpdateDatabase(mycon2);

            mycon2.Close();
			return true;
        }

        public static void UpdateDatabase(SqliteConnection dat)
        {
            //First check to see the version control database exists
            string myquery = "Select name From sqlite_master Where type='table' And name='FILEVERSION'";
            SqliteCommand statement = new SqliteCommand(myquery, dat);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            bool dataavail = statement_reader.Read();
            statement_reader.Close();
            statement.Dispose();
            int database_version = 0;
            if (dataavail == true)
            {
                //Go through the database and find the value
                myquery = "Select version From FILEVERSION";
                statement = new SqliteCommand(myquery, dat);
                statement_reader = statement.ExecuteReader();
                dataavail = statement_reader.Read();
                if (dataavail == true)
                {
                    database_version = Convert.ToInt32(statement_reader["version"].ToString()); //Get the database version
                }
				statement_reader.Close(); //Make sure these are closed
                statement.Dispose();
            }

            if (database_version >= sqldatabase_version) { return; } //No update available
            NebliDexNetLog("Updating database");

            if (database_version == 0)
            {
                //Create the table for database versions
                myquery = "Create Table FILEVERSION (nindex Integer Primary Key ASC, version Integer)";
                statement = new SqliteCommand(myquery, dat);
                statement.ExecuteNonQuery();
                statement.Dispose();
                //Insert a row with version
                myquery = "Insert Into FILEVERSION (version) Values (" + sqldatabase_version + ");";
                statement = new SqliteCommand(myquery, dat);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Now alter table for Validating Transactions
                myquery = "Alter Table VALIDATING_TRANSACTIONS Add Column maker_sendamount Text;";
                statement = new SqliteCommand(myquery, dat);
                statement.ExecuteNonQuery();
                statement.Dispose();

                //Add another column
                myquery = "Alter Table VALIDATING_TRANSACTIONS Add Column taker_receive_add Text;";
                statement = new SqliteCommand(myquery, dat);
                statement.ExecuteNonQuery();
                statement.Dispose();
                database_version++;
            }

			if (database_version == 1)
            {
                //Create Saved Orders data 
                myquery = "Create Table SAVEDORDERS";
                myquery += " (nindex Integer Primary Key ASC, market Integer, type Integer, nonce Text, price Text, amount Text, min_amount Text)";
                statement = new SqliteCommand(myquery, dat);
                statement.ExecuteNonQuery();
                statement.Dispose();
				database_version++;
            }

			if (database_version == 2)
            { //Moving from database version 2 to version 3             
              //Add columns
                myquery = "Alter Table MYTRANSACTIONS Add Column atomic_unlock_time Integer;";
                myquery += " Alter Table MYTRANSACTIONS Add Column atomic_refund_time Integer;";
                myquery += " Alter Table MYTRANSACTIONS Add Column receive_amount Text;";
                myquery += " Alter Table MYTRANSACTIONS Add Column to_add_redeemscript Text;";
                myquery += " Alter Table MYTRANSACTIONS Add Column atomic_secret_hash Text;";
                myquery += " Alter Table MYTRANSACTIONS Add Column atomic_secret Text;";
                statement = new SqliteCommand(myquery, dat);
                statement.ExecuteNonQuery();
                statement.Dispose();

                myquery = "Alter Table VALIDATING_TRANSACTIONS Add Column taker_tx Text;";
                statement = new SqliteCommand(myquery, dat);
                statement.ExecuteNonQuery();
                statement.Dispose();
                database_version++;
            }

            //Future database updates go here

            //Now update the version for the file
            myquery = "Update FILEVERSION Set version = " + sqldatabase_version;
            statement = new SqliteCommand(myquery, dat);
            statement.ExecuteNonQuery();
            statement.Dispose();
        }

        public static string EvaluateNewOrder(DexConnection con, JObject jord, OpenOrder ord)
        {
            //This function will evaluate an order and make sure it is unique
            //It doesn't actual check wallet balance. This has been deferred to validation node.
            string my_ip = getPublicFacingIP();

            //Bad numbers may cause overflows, but since it try catch statement, should not crash program
            ord.order_nonce = jord["order.nonce"].ToString();
            ord.ip_address_port[0] = con.ip_address[0];
            ord.ip_address_port[1] = con.ip_address[1];
            ord.cn_relayer_ip = my_ip; //We are the owner of the order
            ord.market = Convert.ToInt32(jord["order.market"].ToString());
            ord.type = Convert.ToInt32(jord["order.type"].ToString());
			ord.original_amount = Math.Round(Convert.ToDecimal(jord["order.originalamount"].ToString(), CultureInfo.InvariantCulture), 8);
            ord.minimum_amount = Math.Round(Convert.ToDecimal(jord["order.min_amount"].ToString(), CultureInfo.InvariantCulture), 8);
            ord.price = Math.Round(Convert.ToDecimal(jord["order.price"].ToString(), CultureInfo.InvariantCulture), 8);
            ord.amount = ord.original_amount;
            ord.order_stage = 0;
            ord.cooldownend = 0;

			if (ord.order_nonce.Length != 32)
            {
                //Should be 32 characters long
                return "Order Denied: Invalid Order Data";
            }
            if (ord.market < 0 || ord.market >= total_markets)
            {
                return "Order Denied: Invalid Order Data";
            }
            if (ord.type != 0 && ord.type != 1)
            {
                return "Order Denied: Invalid Order Data";
            }
            if (ord.original_amount <= 0)
            {
                return "Order Denied: Amount must be positive";
            }
            if (ord.minimum_amount <= 0)
            {
                return "Order Denied: Minimum amount must be positive";
            }
            if (ord.minimum_amount > ord.original_amount)
            {
                return "Order Denied: Minimum order amount cannot be greater than order amount";
            }
            if (ord.price <= 0)
            {
                return "Order Denied: Price must be positive";
            }
            if (ord.price > max_order_price)
            {
                return "Order Denied: Price is greater than 10 000 000";
            }

			decimal block_fee1 = 0;
            decimal block_fee2 = 0;

            int trade_wallet_blockchaintype = GetWalletBlockchainType(MarketList[ord.market].trade_wallet);
            int base_wallet_blockchaintype = GetWalletBlockchainType(MarketList[ord.market].base_wallet);
            block_fee1 = blockchain_fee[trade_wallet_blockchaintype]; //Trade wallet blockchain fee
            block_fee2 = blockchain_fee[base_wallet_blockchaintype]; //Base wallet blockchain fee   

            //Now calculate the totals for ethereum blockchain
            if (trade_wallet_blockchaintype == 6)
            {
				block_fee1 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[ord.market].trade_wallet));
            }
            if (base_wallet_blockchaintype == 6)
            {
				block_fee2 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[ord.market].base_wallet));
            }

            decimal total = ord.original_amount * ord.price;
            if (total < block_fee2 || ord.original_amount < block_fee1)
            {
                //The trade amount is too small
                return "Order Denied: Total order too small";
            }

            if (MarketList[ord.market].base_wallet == 3 || MarketList[ord.market].trade_wallet == 3)
            {
                //Taker or Maker is sending or receiving ndex
                //The amout must be greater than ndex*2
                if (ord.original_amount < ndex_fee * 2)
                {
                    return "Order Denied: Amount too small based on the current CN fee";
                }
            }

            //Detect if order nonce is unique for all markets
            //We updated the rules to allow for up to [total market] orders on the market
            int order_num = 1;
            for (int market = 0; market < total_markets; market++)
            {
                lock (OpenOrderList[market])
                {
                    for (int i = 0; i < OpenOrderList[market].Count; i++)
                    {
                        if (OpenOrderList[market][i].order_nonce.Equals(ord.order_nonce) == true)
                        {
                            //Someone already has this nonce
                            return "Order Denied: Please resubmit order";
                        }

                        if (OpenOrderList[market][i].cn_relayer_ip.Equals(my_ip) == true)
                        {
                            //We are the first relayer for this order
                            if (OpenOrderList[market][i].ip_address_port[0].Equals(ord.ip_address_port[0]) == true)
                            {
                                if (OpenOrderList[market][i].ip_address_port[1].Equals(ord.ip_address_port[1]) == true)
                                {
                                    order_num++;
                                    if (order_num > total_markets)
                                    {
                                        //There are too many orders by this person on the market
                                        return "Order Denied: Exceeded max amount of open orders at one time.";
                                    }
                                }
                            }
                        }
                    }
                }
            }

            lock (OpenOrderList[ord.market])
            {
                //If everything is good, add order
                OpenOrderList[ord.market].Add(ord);
                //Add to market list
                if (main_window_loaded == true)
                {
                    main_window.AddOrderToView(ord);
                }
                return "Order OK";
            }
        }

        public static bool EvaluateRelayedOrder(DexConnection con, JObject jord, OpenOrder ord, bool cnmode)
        {
            //This function will evaluate a relayed order for addition to market
            //It doesn't actual check wallet balance. This has been deferred to validation node.

            //Bad numbers may cause overflows, but since it try catch statement, should not crash program
            ord.order_nonce = jord["order.nonce"].ToString();
            ord.market = Convert.ToInt32(jord["order.market"].ToString());
            ord.type = Convert.ToInt32(jord["order.type"].ToString());
			ord.original_amount = Math.Round(Convert.ToDecimal(jord["order.originalamount"].ToString(), CultureInfo.InvariantCulture), 8);
            ord.price = Math.Round(Convert.ToDecimal(jord["order.price"].ToString(), CultureInfo.InvariantCulture), 8);
            ord.amount = ord.original_amount;
            ord.minimum_amount = Math.Round(Convert.ToDecimal(jord["order.min_amount"].ToString(), CultureInfo.InvariantCulture), 8);
			ord.order_stage = 0;
            ord.cooldownend = 0;

            //Check if cancellation token present
            lock (CancelOrderTokenList)
            {
                for (int i = 0; i < CancelOrderTokenList.Count; i++)
                {
                    if (CancelOrderTokenList[i].Equals(ord.order_nonce) == true)
                    {
                        //This order was previously cancelled by token
                        CancelOrderTokenList.RemoveAt(i); //Take out of list
                        return false; //Do not relay/add this order
                    }
                }
            }

			if (ord.order_nonce.Length != 32)
            {
                //Should be 32 characters long
                return false;
            }
            if (ord.market < 0 || ord.market >= total_markets)
            {
                return false;
            }
            if (ord.type != 0 && ord.type != 1)
            {
                return false;
            }
            if (ord.original_amount < 0)
            {
                return false;
            }
            if (ord.minimum_amount <= 0)
            {
                return false;
            }
            if (ord.minimum_amount > ord.original_amount)
            {
                return false;
            }
            if (ord.price <= 0)
            {
                return false;
            }
            if (ord.price > max_order_price)
            {
                return false;
            }

            if (cnmode == true)
            {
                ord.ip_address_port[0] = jord["order.ip"].ToString();
                ord.ip_address_port[1] = jord["order.port"].ToString();
                ord.cn_relayer_ip = jord["order.cn_ip"].ToString();
            }

            //Detect if order nonce is unique for all markets
            int order_num = 1;
            for (int market = 0; market < total_markets; market++)
            {
                lock (OpenOrderList[market])
                {
                    for (int i = 0; i < OpenOrderList[market].Count; i++)
                    {
                        if (OpenOrderList[market][i].order_nonce.Equals(ord.order_nonce) == true)
                        {
                            //Someone already has this nonce
                            return false;
                        }

                        if (cnmode == true)
                        {
                            if (OpenOrderList[market][i].cn_relayer_ip.Equals(ord.cn_relayer_ip) == true)
                            {
                                //Check if this order was already relayed
                                if (OpenOrderList[market][i].ip_address_port[0].Equals(ord.ip_address_port[0]) == true)
                                {
                                    if (OpenOrderList[market][i].ip_address_port[1].Equals(ord.ip_address_port[1]) == true)
                                    {
                                        order_num++;
                                        if (order_num > total_markets + 2)
                                        {
                                            //This will allow for temporary overlaps in case of lagged close orders
                                            //Too many orders on the market by this user
                                            return false;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            lock (OpenOrderList[ord.market])
            {
                //If everything is good, add order
                if (critical_node == true || ord.market == exchange_market)
                {
                    OpenOrderList[ord.market].Add(ord);
                }
                //Add to market list
                if (main_window_loaded == true)
                {
                    main_window.AddOrderToView(ord);
                }
                return true;
            }
        }

		public static string EvaluateNewOrderRequest(DexConnection con, JObject jord, OrderRequest req)
        {
            //Evaluates the new order request received          
            req.market = Convert.ToInt32(jord["order.market"].ToString());
            req.order_nonce_ref = jord["order.nonce"].ToString();
            req.type = Convert.ToInt32(jord["order.type"].ToString());
            req.utctime = UTCTime();
            req.order_stage = 0; //Just received order
            req.request_id = GenerateHexNonce(12); //Unique ID for this request (created in case of simultaneous trades made)

            decimal amount = Math.Round(Convert.ToDecimal(jord["order.originalamount"].ToString(), CultureInfo.InvariantCulture), 8);
            //The amount of trade amount involved in trade

            //Get takers address from and receive
            req.from_add_1 = jord["taker.from_add"].ToString();
            req.to_add_2 = jord["taker.to_add"].ToString();

            //The CN that receives the initial request will be held responsible for making sure there is a response
            //from the maker of the market
            req.ip_address_taker[0] = con.ip_address[0];
            req.ip_address_taker[1] = con.ip_address[1];
            req.taker_cn_ip = getPublicFacingIP(); //We are the requester's owner CN

            //Now verify the request
			if (req.order_nonce_ref.Length != 32)
            {
                //Should be 32 characters long
                return "Order Request Denied: Invalid Order Data";
            }
            if (req.from_add_1.Length > 100 || req.to_add_2.Length > 100)
            {
                return "Order Request Denied: Invalid Order Data";
            }
            if (req.market < 0 || req.market >= total_markets)
            {
                return "Order Request Denied: Invalid Order Data";
            }
            if (req.type != 0 && req.type != 1)
            {
                return "Order Request Denied: Invalid Order Data";
            }
            if (amount < 0)
            {
                return "Order Request Denied: Amount must be positive";
            }

            //Because tokens are indivisible at the moment, amounts can only be in whole numbers
			bool ntp1_wallet = IsWalletNTP1(MarketList[req.market].trade_wallet);
            if (ntp1_wallet == true)
            {
                if (Math.Abs(Math.Round(amount) - amount) > 0)
                {
                    return "Order Request Denied: Token must be indivisible.";
                }
            }

            //Confirm this order hasn't already received a request
            lock (OrderRequestList)
            {
                for (int i = 0; i < OrderRequestList.Count; i++)
                {
                    if (OrderRequestList[i].order_stage != 3)
                    { //Closed Order
                        if (OrderRequestList[i].order_nonce_ref.Equals(req.order_nonce_ref) == true)
                        {
                            return "Order Request Denied: There is already an open request on this order right now. Try again later.";
                        }

                        if (OrderRequestList[i].from_add_1.Equals(req.from_add_1) == true)
                        {
                            //The taker is already involved in an unclosed trade
                            return "Order Request Denied: You already have an open order you are currently filling. Try again later.";
                        }
                    }
                }
            }

            //Good, new request for this order
            bool order_exist = false;
            lock (OpenOrderList[req.market])
            {
                for (int i = 0; i < OpenOrderList[req.market].Count; i++)
                {
                    if (OpenOrderList[req.market][i].order_nonce.Equals(req.order_nonce_ref) == true)
                    {
                        //Found the order
                        order_exist = true;
                        if (OpenOrderList[req.market][i].cn_relayer_ip == req.taker_cn_ip)
                        {
                            if (OpenOrderList[req.market][i].ip_address_port[0] == req.ip_address_taker[0])
                            {
                                if (OpenOrderList[req.market][i].ip_address_port[1] == req.ip_address_taker[1])
                                {
                                    return "Order Request Denied: This is your order";
                                }
                            }
                        }

                        //Get information from the order
                        if (OpenOrderList[req.market][i].type == req.type)
                        {
                            //Should have opposite type
                            return "Order Request Denied: Invalid Order Data";
                        }

                        if (OpenOrderList[req.market][i].order_stage > 0)
                        {
                            //Order already in trade
                            return "Order Request Denied: Order already in trade";
                        }

                        //Make sure amount does not exceed allowable or less than minimum
                        decimal max = OpenOrderList[req.market][i].amount;
                        decimal min = OpenOrderList[req.market][i].minimum_amount;
                        if (max < min) { min = max; }
                        if (amount < min)
                        {
                            return "Order Request Denied: Amount less than minimum";
                        }
                        else if (amount > max)
                        {
                            return "Order Request Denied: Amount more than order amount";
                        }

                        req.ndex_fee = ndex_fee; //The current NDEX fee is required to pay                      
                        req.ip_address_maker[0] = OpenOrderList[req.market][i].ip_address_port[0];
                        req.ip_address_maker[1] = OpenOrderList[req.market][i].ip_address_port[1];
                        req.maker_cn_ip = OpenOrderList[req.market][i].cn_relayer_ip;

                        //Calculate who decides to validate
                        int portcombo = Convert.ToInt32(req.ip_address_taker[1]) + Convert.ToInt32(req.ip_address_maker[1]);
                        req.who_validate = portcombo % 2; //Will be either 0 or 1
                                                          //0 maker validates, 1 taker validates

                        decimal price = OpenOrderList[req.market][i].price;
                        decimal taker_receive = 0;
                        decimal maker_receive = 0;

                        //Calculate the amounts to be send from who to who
                        int taker_sendwallet = 0;
						int maker_sendwallet = 0;
                        if (req.type == 0)
                        {
                            //Taker is buying trade market account
                            taker_receive = amount; //The amount of trade symbol taker is getting
                            maker_receive = Math.Round(amount * price, 8); //The amount of base symbol maker is getting
                            taker_sendwallet = MarketList[req.market].base_wallet;
							maker_sendwallet = MarketList[req.market].trade_wallet;
                        }
                        else
                        {
                            taker_receive = Math.Round(amount * price, 8); //The amount of base symbol taker is getting
                            maker_receive = amount; //The amount of trade symbol maker is getting
                            taker_sendwallet = MarketList[req.market].trade_wallet;
							maker_sendwallet = MarketList[req.market].base_wallet;
                        }

                        if (MarketList[req.market].base_wallet == 3 || MarketList[req.market].trade_wallet == 3)
                        {
                            //Taker or Maker is sending or receiving ndex
                            //The amout must be greater than ndex*2
                            if (amount < ndex_fee * 2)
                            {
                                return "Order Request Denied: Amount too small based on the current CN fee";
                            }
                        }

                        //Make sure that the order is large enough to move
						decimal block_fee1 = 0;
                        decimal block_fee2 = 0;
                        int trade_wallet_blockchaintype = GetWalletBlockchainType(MarketList[req.market].trade_wallet);
                        int base_wallet_blockchaintype = GetWalletBlockchainType(MarketList[req.market].base_wallet);
                        block_fee1 = blockchain_fee[trade_wallet_blockchaintype]; //Trade blockchain fee
                        block_fee2 = blockchain_fee[base_wallet_blockchaintype]; //Base blockchain fee

                        //Now calculate the totals for ethereum blockchain
                        if (trade_wallet_blockchaintype == 6)
                        {
							block_fee1 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[req.market].trade_wallet));
                        }
                        if (base_wallet_blockchaintype == 6)
                        {
							block_fee2 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[req.market].base_wallet));
                        }

                        if (amount < block_fee1 || (amount * price) < block_fee2)
                        {
                            //The trade amount is too small
                            return "Order Request Denied: This order request is too small";
                        }

                        req.amount_1 = maker_receive; //This is amount taker is sending to maker
                        req.amount_2 = taker_receive; //This is amount maker is sending to taker

                        //Verify the taker has this amount to send
                        decimal taker_balance = GetBlockchainAddressBalance(taker_sendwallet, req.from_add_1, false);
                        if (taker_balance < req.amount_1)
                        {
                            //Not enough taker balance to match request
                            return "Order Request Denied: You do not have enough balance to match this order";
                        }
						//And verify if taker is interacting with ethereum chain that he/she has enough ether
                        if (trade_wallet_blockchaintype == 6 || base_wallet_blockchaintype == 6)
                        {
                            if (GetWalletBlockchainType(taker_sendwallet) != 6)
                            {
                                //The Taker is not sending Ethereum but still needs some to interact with contract
                                decimal taker_eth = GetBlockchainAddressBalance(GetWalletType(6), req.to_add_2, false);
								if (taker_eth < GetEtherContractRedeemFee(Wallet.CoinERC20(maker_sendwallet)))
                                {
                                    //Not enough Ethereum in taker account to interact with contract
                                    return "Order Request Denied: You do not have enough balance to match this order in the Ethereum contract";
                                }
							}
							else
                            {
                                //The taker is sending ETH
                                if (Wallet.CoinERC20(taker_sendwallet) == true)
                                {
                                    //Make sure that the taker has an allowance greater than or equal to what he/she is sending
                                    if (GetERC20AtomicSwapAllowance(req.from_add_1, ERC20_ATOMICSWAP_ADDRESS, taker_sendwallet) < req.amount_1)
                                    {
                                        //Allowance is too small
                                        return "Order Request Denied: You have not authorized enough allowance to complete this trade";
                                    }
                                }
                            }
                        }

                        //Maker and taker will of course double check this amount

                        break;
                    }
                }
            }

            if (order_exist == false)
            {
                return "Order Request Denied: Original Order Doesn't Exist";
            }

            //Everything checks out
            lock (OrderRequestList)
            {
                OrderRequestList.Add(req); //Add the new request
            }

            //Will put order stage as one
            ExchangeWindow.PendOrder(req.order_nonce_ref);

            return "Order Request OK";
        }

		public static bool EvaluateRelayOrderRequest(JObject jord)
        {
            //Evaluates the new order request received
            OrderRequest req = new OrderRequest();
            req.market = Convert.ToInt32(jord["request.market"].ToString());
            req.order_nonce_ref = jord["request.order_nonce"].ToString();
            req.type = Convert.ToInt32(jord["request.type"].ToString());
            req.utctime = Convert.ToInt32(jord["request.time"].ToString());
            req.order_stage = 0; //Just received the order request
            req.ndex_fee = Convert.ToDecimal(jord["request.fee"].ToString(), CultureInfo.InvariantCulture);
            req.from_add_1 = jord["request.taker_from_add"].ToString();
            req.to_add_2 = jord["request.taker_to_add"].ToString();
            req.amount_1 = Convert.ToDecimal(jord["request.amount_1"].ToString(), CultureInfo.InvariantCulture);
            req.amount_2 = Convert.ToDecimal(jord["request.amount_2"].ToString(), CultureInfo.InvariantCulture);
            req.ip_address_maker[0] = jord["request.maker_ip"].ToString();
            req.ip_address_maker[1] = jord["request.maker_port"].ToString();
            req.ip_address_taker[0] = jord["request.taker_ip"].ToString();
            req.ip_address_taker[1] = jord["request.taker_port"].ToString();
            req.maker_cn_ip = jord["request.maker_cn_ip"].ToString();
            req.taker_cn_ip = jord["request.taker_cn_ip"].ToString();
            req.request_id = jord["request.request_id"].ToString();

            //Calculate who decides to validate
            int portcombo = Convert.ToInt32(req.ip_address_taker[1]) + Convert.ToInt32(req.ip_address_maker[1]);
            req.who_validate = portcombo % 2; //Will be either 0 or 1

            //0 maker validates, 1 taker validates

            //Now verify the request
            if (req.market < 0 || req.market >= total_markets)
            {
                return false;
            }
            if (req.type != 0 && req.type != 1)
            {
                return false;
            }
            if (req.amount_1 < 0 || req.amount_2 < 0)
            {
                return false;
            }

            string my_ip = getPublicFacingIP();

            //Confirm this order hasn't already received a request
            //This will prevent duplicate request from occuring on a particular CN, it doesn't stop CN network from having 
            lock (OrderRequestList)
            {
                for (int i = 0; i < OrderRequestList.Count; i++)
                {
                    if (OrderRequestList[i].order_stage != 3)
                    { //Closed Order
                        if (OrderRequestList[i].order_nonce_ref.Equals(req.order_nonce_ref) == true)
                        {
                            if (my_ip.Equals(OrderRequestList[i].maker_cn_ip) == true)
                            {
                                //We are the gatekeeper to the maker, so we are more strict accepting order requests
                                NebliDexNetLog("The order request already exists for maker (" + req.order_nonce_ref + ")!");
                                return false;
                            }
                            else
                            {
                                //Allow the remaining nodes to propagate the request
                                if (OrderRequestList[i].request_id.Equals(req.request_id) == true)
                                {
                                    //Unless it has exactly the same request ID
                                    NebliDexNetLog("This exact order request already exists (" + req.order_nonce_ref + ")!");
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            //Good, new request for this order
            bool order_exist = false;
            lock (OpenOrderList[req.market])
            {
                for (int i = 0; i < OpenOrderList[req.market].Count; i++)
                {
                    if (OpenOrderList[req.market][i].order_nonce.Equals(req.order_nonce_ref) == true)
                    {
                        //Found the order
                        order_exist = true;
                        if (OpenOrderList[req.market][i].cn_relayer_ip == req.taker_cn_ip)
                        {
                            if (OpenOrderList[req.market][i].ip_address_port[0] == req.ip_address_taker[0])
                            {
                                if (OpenOrderList[req.market][i].ip_address_port[1] == req.ip_address_taker[1])
                                {
                                    return false;
                                }
                            }
                        }

                        //Get information from the order
                        if (OpenOrderList[req.market][i].type == req.type)
                        {
                            //Should have opposite type
                            return false;
                        }
                        //Not as thorough check as the new order request

                        break;
                    }
                }
            }

            if (order_exist == false)
            {
                return false;
            }

            //Everything checks out
            lock (OrderRequestList)
            {
                OrderRequestList.Add(req); //Add the new request
            }

            //Will put order stage as one
            ExchangeWindow.PendOrder(req.order_nonce_ref);

            return true;
        }

        public static bool VerifyTradeRequest(JObject tjs)
        {
            //We will verify the trade request against our open orders
            string nonce = tjs["cn.order_nonce"].ToString();
            decimal amount = Convert.ToDecimal(tjs["cn.amount"].ToString(), CultureInfo.InvariantCulture);

            //Find my open order matching this nonce
            OpenOrder myord = null;
            lock (MyOpenOrderList)
            {
                for (int i = 0; i < MyOpenOrderList.Count; i++)
                {
                    if (MyOpenOrderList[i].order_nonce.Equals(nonce) == true && MyOpenOrderList[i].is_request == false && MyOpenOrderList[i].queued_order == false)
                    {
                        //Found the order, it matches
                        myord = MyOpenOrderList[i]; break;
                    }
                }
            }

            if (myord == null) { return false; }
            if (amount > myord.amount) { return false; } //Somehow amount was too big
            decimal min_amount = myord.minimum_amount;
            if (min_amount > myord.amount) { min_amount = myord.amount; }
            if (amount < min_amount) { return false; } //Request size too small

            int sendwallet = 0;
            int receivewallet = 0; //Upload the addresses for the maker
            decimal sendamount = 0;
            if (myord.type == 0)
            {
                sendwallet = MarketList[myord.market].base_wallet;
                receivewallet = MarketList[myord.market].trade_wallet;
                sendamount = Math.Round(amount * myord.price, 8);
            }
            else
            {
                sendwallet = MarketList[myord.market].trade_wallet;
                receivewallet = MarketList[myord.market].base_wallet;
                sendamount = amount;
            }

			bool ntp1_wallet = IsWalletNTP1(sendwallet);
            if (ntp1_wallet == false)
            {
                //I am going to send a non-token, get the blockchain fee
                int wallet_blockchaintype = GetWalletBlockchainType(sendwallet);
                if (wallet_blockchaintype != 6)
                {
                    if (sendamount < blockchain_fee[wallet_blockchaintype]) { return false; } //Request is too small
                }
                else
                {
					if (sendamount < GetEtherContractTradeFee(Wallet.CoinERC20(sendwallet))) { return false; }
                }
            }
            else
            {
                //Sending a token
                if (sendamount < 1) { return false; } //Cannot send 0 token
                                                      //Also check to make sure token amount is whole number
                if (Math.Abs(Math.Round(sendamount) - sendamount) > 0)
                {
                    return false;
                }
            }

            if (sendwallet == 3)
            {
                //Sending NDEX
                //Make sure its at least twice the ndex fee
                if (amount < ndex_fee * 2)
                {
                    return false;
                }
            }

            tjs["trade.maker_send_add"] = GetWalletAddress(sendwallet);
            tjs["trade.maker_receive_add"] = GetWalletAddress(receivewallet);

            //This function will also evaluate whether the wallet is available or not
            string msg = "";
            bool walletavail = CheckWalletBalance(sendwallet, sendamount, ref msg);
            if (walletavail == false)
            { //No money or not available
                return false;
            }

            bool fees_ok = CheckMarketFees(myord.market, myord.type, sendamount, ref msg, false);
            if (fees_ok == false)
            { //Not enough to cover the fees
                return false;
            }

            myord.validating = false; //Reset the validation value

            if (myord.queued_order == true) { return false; }

            //Now we will queue all the other open orders
            if (MyOpenOrderList.Count > 1)
            {
                QueueAllButOpenOrders(myord); //Queues all but this one
            }

            if (myord.queued_order == true)
            {
                NebliDexNetLog("Race condition detected between two orders");
                return false;
            } //Used in case of race condition and both get queued

            return true;
        }

		public static bool VerifyTradeConnection(DexConnection con, JObject js)
        {
            //This will make sure that the connection is coming from some involved in the trade
            //The closest CN will be doing this, so it was the one who initially created the IPs
            string ip = con.ip_address[0];
            string port = con.ip_address[1];
            if (js["cn.method"].ToString() == "cn.tradeavail")
            {
                //This is a message from the maker
                lock (OrderRequestList)
                {
                    for (int i = 0; i < OrderRequestList.Count; i++)
                    {
                        if (OrderRequestList[i].order_nonce_ref.Equals(js["cn.order_nonce"].ToString()) == true && OrderRequestList[i].order_stage != 3)
                        {
                            if (OrderRequestList[i].ip_address_maker[0] != ip || OrderRequestList[i].ip_address_maker[1] != port)
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            return true;
        }

        public static void RemoveMyOrderNoLock(OpenOrder ord)
        {
            lock (OpenOrderList[ord.market])
            {
                for (int i = OpenOrderList[ord.market].Count - 1; i >= 0; i--)
                {
                    //Remove any order that matches our nonce
                    if (OpenOrderList[ord.market][i].order_nonce.Equals(ord.order_nonce) == true && OpenOrderList[ord.market][i].is_request == ord.is_request)
                    {
                        OpenOrderList[ord.market].RemoveAt(i);
                    }
                }
            }

            //Now remove the order from view
            if (main_window_loaded == true)
            {
                main_window.RemoveOrderFromView(ord);
            }
        }

        public static void CheckWallet(Window win)
        {
			//Wallet version is now 1

            //This function checks wallet format and if encryption present
            if (File.Exists(App_Path + "/data/account.dat") == false)
            {
                if (run_headless == true)
                {
                    Console.WriteLine("Cannot run headless on a new account");
                    Headless_Application_Close();
                }
                //Prompt user to enter password for future wallet
				ManualResetEvent waiter = new ManualResetEvent(false);
				UserPromptWindow p=null;
				Application.Invoke(delegate
                {
					p = new UserPromptWindow("Please enter a password for your new\nwallet. (Leave blank if one not desired)", false); //Window
                    p.Parent = win;
                    p.Modal = true;
					p.waiting = waiter;
                    p.Show();
                });
				waiter.WaitOne();

				if(p != null){
					my_wallet_pass = p.final_response;
				}            
            }
            else
            {
                //File does exist, check wallet if password does exist
                int version = 0;
                int encrypted = 0;

                using (System.IO.StreamReader file =
                    new System.IO.StreamReader(@App_Path + "/data/account.dat", false))
                {
                    string first_line = file.ReadLine(); //In old version, this is master key
                    version = Convert.ToInt32(first_line);
                    encrypted = Convert.ToInt32(file.ReadLine());
                }

                if (encrypted > 0)
                {
                    NebliDexNetLog("Wallet is encrypted");
                    //Need the password to decrypt the wallet
                    if (run_headless == true)
                    {
                        Console.WriteLine("Please enter your wallet password: ");
                        my_wallet_pass = Console.ReadLine();
                        Console.Clear(); //Remove sensitive info from screen
                        Console.WriteLine("Running NebliDex Program, version: " + version_text);
                    }
                    else
                    {
						ManualResetEvent waiter = new ManualResetEvent(false);
                        UserPromptWindow p = null;
                        Application.Invoke(delegate
                        {
							p = new UserPromptWindow("\nPlease enter your wallet password.", true); //Window
                            p.Parent = win;
                            p.Modal = true;
                            p.waiting = waiter;
                            p.Show();
                        });
                        waiter.WaitOne();

                        if (p != null)
                        {
                            my_wallet_pass = p.final_response;
                        }
                    }
                }
				if (version < accountdat_version)
                {
                    NebliDexNetLog("Wallet needs to be upgraded");
                }
            }
        }

        public static bool VerifyWalletPassword(string privkey, string address, int type)
        {
            //This will return true if the wallet was decrypted successfully
			if (GetWalletBlockchainType(type) == 6)
            {
                //Eth
                string my_eth_add = GenerateEthAddress(privkey);
                if (my_eth_add.Equals(address) == true)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            try
            {
                lock (transactionLock)
                {
                    //Prevent more than one thread from using the network
                    Network my_net;
                    if (testnet_mode == false)
                    {
                        my_net = Network.Main;
                    }
                    else
                    {
                        my_net = Network.TestNet;
                    }

                    //Change the network
                    ChangeVersionByte(type, ref my_net); //NEBL network
                    ExtKey priv_key = ExtKey.Parse(privkey, my_net);
                    string my_add = priv_key.PrivateKey.PubKey.GetAddress(my_net).ToString();
                    if (address.Equals(my_add) == true)
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Error decrypting wallet: " + e.ToString());
            }
            NebliDexNetLog("Failed to open wallet");
            return false;
        }

        public static bool LoadWallet()
        {
            //The the wallet for this user or create if none exists
            WalletList.Clear(); //Remove old wallet list

            if (File.Exists(App_Path + "/data/account.dat") == false)
            {
                //Create the wallet

                //Using statement closes file if error occurs
                using (System.IO.StreamWriter file =
                    new System.IO.StreamWriter(@App_Path + "/data/account.dat", false))
                {
                    int i;
					file.WriteLine(accountdat_version); //Version of account file
                    if (my_wallet_pass.Length > 0)
                    {
                        file.WriteLine(1); //This file will be encrypted
                    }
                    else
                    {
                        file.WriteLine(0); //No encryption present
                    }
					for (i = 0; i < total_cointypes; i++)
                    {
						//Create wallet accounts
                        //Only blockchains involved
                        string masterkey = GenerateMasterKey();
                        if (my_wallet_pass.Length > 0)
                        {
                            file.WriteLine(AESEncrypt(masterkey, my_wallet_pass));
                        }
                        else
                        {
                            file.WriteLine(masterkey);
                        }
                        file.WriteLine(i); //Wallet blockchain type
                        file.WriteLine("1"); //Only 1 account
                        ExtKey priv_key = GeneratePrivateKey(masterkey, 0);
                        Network my_net;
                        if (testnet_mode == false)
                        {
                            my_net = Network.Main;
                        }
                        else
                        {
                            my_net = Network.TestNet;
                        }
                        string privatekey = priv_key.ToString(my_net);
						string myaddress = GenerateCoinAddress(priv_key, GetWalletType(i));
                        if (i == 6)
                        {
                            //Calculate the Ethereum address from the private key
                            myaddress = GenerateEthAddress(privatekey);
                        }
                        if (my_wallet_pass.Length > 0)
                        {
                            privatekey = AESEncrypt(privatekey, my_wallet_pass);
                        }
                        file.WriteLine(privatekey);
                        file.WriteLine(myaddress);
                    }
                    file.Flush();
                }
            }

            //Now load the wallet information
			int this_wallet_version = 0;
            using (System.IO.StreamReader file2 =
                new System.IO.StreamReader(@App_Path + "/data/account.dat", false))
            {
				int i;
                this_wallet_version = Convert.ToInt32(file2.ReadLine()); //Wallet version
                int max_wallets = total_cointypes;
                if (this_wallet_version == 0)
                {
                    max_wallets = 3;
                }
                int enc = Convert.ToInt32(file2.ReadLine());
				for (i = 0; i < max_wallets; i++)
                {
					//Load the wallet
                    Wallet wal = new Wallet();
                    file2.ReadLine(); //Skip the master key
					wal.blockchaintype = Convert.ToInt32(file2.ReadLine()); //Get the wallet blockchain type
                    wal.type = GetWalletType(wal.blockchaintype);

                    int amount = Convert.ToInt32(file2.ReadLine()); //Amounts of addresses
                    for (int i2 = 1; i2 <= amount; i2++)
                    {
                        if (i2 == amount)
                        {
                            //This is the one we want
                            wal.private_key = file2.ReadLine();
                            wal.address = file2.ReadLine();
                            if (enc > 0)
                            {
                                //This wallet is encrypted so decrypt it
                                bool good = true;
                                try
                                {
                                    //This might fail if the wallet cannot be decrypted
                                    wal.private_key = AESDecrypt(wal.private_key, my_wallet_pass);
                                }
                                catch (Exception)
                                {
                                    NebliDexNetLog("Failed to decrypt this wallet");
                                    good = false;
                                }

                                //Now verify if the wallet was decrypted successfully
                                if (good == true)
                                {
                                    good = VerifyWalletPassword(wal.private_key, wal.address, wal.type);
                                    if (good == false)
                                    {
                                        NebliDexNetLog("Verify password failed.");
                                    }
                                }

                                if (good == false)
                                {
									if (run_headless == false)
                                    {
										Application.Invoke(delegate
                                        {
											MessageBox(null, "Error!", "Failed to decrypt your wallet due to wrong password!", "OK");
                                            Application.Quit();
                                        });

										return false;
                                    }
                                    else
                                    {
                                        Console.WriteLine("Failed to decrypt your wallet due to wrong password!");
                                        Headless_Application_Close();
                                    }
									return false;
                                }
                            }
                        }
                        else
                        {
                            file2.ReadLine();
                            file2.ReadLine(); //Skip these lines
                        }
                    }

                    if (WalletList.Count == 0)
                    {
                        WalletList.Add(wal); //Add to the wallet list
                    }
                    else
                    {
                        //Load BTC after Neblio and LTC after BTC
						WalletList.Insert(wal.blockchaintype, wal);
                    }
                    if (wal.type == 0)
                    { //NDEX and other NTP1 tokens
                      //Load all NEBL based tokens from this private key

                        //NDEX based on NEBL
                        //Make a clone of this wallet but with a different type
						for (int i2 = 0; i2 < Wallet.total_coin_num; i2++)
                        {
                            if (Wallet.CoinNTP1(i2) == true && Wallet.CoinActive(i2) == true)
                            {
                                //Wallet is Active and NTP1, add it to neblio wallet address                                
                                Wallet wal2 = new Wallet();
                                wal2.type = i2;
                                wal2.private_key = wal.private_key;
                                wal2.address = wal.address;
                                wal2.blockchaintype = 0; //Neblio based
                                WalletList.Add(wal2);
                            }
                        }
					}else if (wal.type == 17)
                    {
                        //ERC20 Tokens
                        for (int i2 = 0; i2 < Wallet.total_coin_num; i2++)
                        {
                            if (Wallet.CoinERC20(i2) == true && Wallet.CoinActive(i2) == true)
                            {
                                //Wallet is Active and ERC20, add it to ethereum wallet address                             
                                Wallet wal2 = new Wallet();
                                wal2.type = i2;
                                wal2.private_key = wal.private_key;
                                wal2.address = wal.address;
                                wal2.blockchaintype = 6; //ETH based
                                WalletList.Add(wal2);
                            }
                        }
                    }
                }
            }

			if (this_wallet_version < accountdat_version)
            {
                NebliDexNetLog("Upgrading the wallet now");
                string[] wallet_lines = File.ReadAllLines(@App_Path + "/data/account.dat"); //Get all the lines from the current account
                wallet_lines[0] = accountdat_version.ToString();
                File.WriteAllLines(@App_Path + "/data/account_new.dat", wallet_lines); //Copys information to new file
                if (this_wallet_version == 0)
                {
                    //Update it by adding new wallets
                    int pos = 3;
                    int max_wallet = 7;
                    using (System.IO.StreamWriter file =
                        new System.IO.StreamWriter(@App_Path + "/data/account_new.dat", true))
                    {
                        for (int i = pos; i < max_wallet; i++)
                        {
                            //Create wallet accounts
                            //Only blockchains involved
                            string masterkey = GenerateMasterKey();
                            if (my_wallet_pass.Length > 0)
                            {
                                file.WriteLine(AESEncrypt(masterkey, my_wallet_pass));
                            }
                            else
                            {
                                file.WriteLine(masterkey);
                            }
                            file.WriteLine(i); //Wallet blockchain type
                            file.WriteLine("1"); //Only 1 account
                            ExtKey priv_key = GeneratePrivateKey(masterkey, 0);
                            Network my_net;
                            if (testnet_mode == false)
                            {
                                my_net = Network.Main;
                            }
                            else
                            {
                                my_net = Network.TestNet;
                            }
                            string privatekey = priv_key.ToString(my_net);
                            string myaddress = GenerateCoinAddress(priv_key, GetWalletType(i));
                            if (i == 6)
                            {
                                //Calculate the Ethereum address from the private key
                                myaddress = GenerateEthAddress(privatekey);
                            }
                            //And now add the wallet files
                            Wallet wal = new Wallet();
                            wal.blockchaintype = i;
                            wal.type = GetWalletType(i);
                            wal.address = myaddress;
                            wal.private_key = privatekey; //Get the private key before it is encrypted
                            WalletList.Insert(wal.blockchaintype, wal);
                            if (my_wallet_pass.Length > 0)
                            {
                                privatekey = AESEncrypt(privatekey, my_wallet_pass);
                            }
                            file.WriteLine(privatekey);
                            file.WriteLine(myaddress);

							//Add the ERC20 tokens
                            if (wal.type == 17)
                            {
                                //ERC20 Tokens
                                for (int i2 = 0; i2 < Wallet.total_coin_num; i2++)
                                {
                                    if (Wallet.CoinERC20(i2) == true && Wallet.CoinActive(i2) == true)
                                    {
                                        //Wallet is Active and ERC20, add it to ethereum wallet address                             
                                        Wallet wal2 = new Wallet();
                                        wal2.type = i2;
                                        wal2.private_key = wal.private_key;
                                        wal2.address = wal.address;
                                        wal2.blockchaintype = 6; //ETH based
                                        WalletList.Add(wal2);
                                    }
                                }
                            }
                        }
                        file.Flush();
                    }
                    this_wallet_version++;
                }

                //Future versions will go here

                //Move the files
                if (File.Exists(App.App_Path + "/data/account_new.dat") != false)
                {
                    File.Delete(App.App_Path + "/data/account.dat");
                    File.Move(App.App_Path + "/data/account_new.dat", App.App_Path + "/data/account.dat");
                }

                //Also delete the old electrum nodes list as the client will find the new nodes
                if (File.Exists(App_Path + "/data/electrum_peers.dat") == true)
                {
                    File.Delete(App_Path + "/data/electrum_peers.dat");
                }
            }
			return true;
        }

        public static bool CheckPendingPayment()
        {
            if (running_consolidation_check == true) { return true; } //Wallet is checking for too many UTXOs

            //This will return true if there is a pending payment being processed
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select rows that have not been finalized
            string myquery = "Select nindex From MYTRANSACTIONS Where type < 3";
            statement = new SqliteCommand(myquery, mycon);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            bool dataavail = statement_reader.Read();
            statement_reader.Close();
            statement.Dispose();
            mycon.Close();

            return dataavail;
        }

		public static bool CheckPendingTrade()
        {
            //This will return true if there is a pending payment being processed
            SqliteConnection mycon = new SqliteConnection("Data Source=\""+App.App_Path+"/data/neblidex.db\";Version=3;");
            mycon.Open();
            
            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000",mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();
            
            //Now select trade history that have not been finalized
            string myquery = "Select nindex From MYTRADEHISTORY Where pending = 1";
            statement = new SqliteCommand(myquery,mycon);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            bool dataavail = statement_reader.Read();
            statement_reader.Close();
            statement.Dispose();            
            mycon.Close();

            return dataavail;           
        } 

        public static void ChangeWalletAddresses()
        {
            //This will attempt to change all the wallet addresses to a new address

            //Check if any pending payments from recent trades
            bool dataavail = CheckPendingPayment();

            if (dataavail == true)
            {
                //There are pending payments
				Application.Invoke(delegate
                {
					MessageBox(null, "Notice", "There is at least one pending payment to this current address", "OK");
                });
                return;
            }

            bool moveable = true;
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].status != 0)
                {
                    moveable = false; break;
                }
            }

            if (moveable == false)
            {
				Application.Invoke(delegate
                {
					MessageBox(null, "Notice", "There is at least one wallet unavailable to change the current address", "OK");
                });
                return;
            }

			//TODO: Figure out way to transfer ETH tokens as well in one transaction, otherwise, skip ETH transfer and tell user
            bool skip_eth = false;
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (Wallet.CoinERC20(WalletList[i].type) == true)
                {
                    if (WalletList[i].balance > 0)
                    {
                        skip_eth = true; break; //There are tokens present, cannot change eth address
                    }
                }
            }

			if (skip_eth == true)
            {
				Application.Invoke(delegate
                {
					MessageBox(null, "Notice", "Changing all addresses except Ethereum due to ERC20 tokens present at address", "OK");
                });
            }

            //This function will add a private key (with address as well) for all wallets 1 by 1
			for (int i = 0; i < total_cointypes; i++)
            {

                //So first do a test transaction to make sure the transaction can be created
                int wallet_type = GetWalletType(i);
                string curr_add = GetWalletAddress(wallet_type);
                decimal bal = 0;
                Transaction testtx = null;
                Nethereum.Signer.TransactionChainId testeth_tx = null;
                if (wallet_type == 0)
                { //Neblio
                    testtx = CreateNTP1AllTokenTransfer(curr_add);
                }
                else
                {
                    bal = GetWalletAmount(wallet_type); //Get blockchain balance
                    if (bal > 0)
                    {
                        if (i != 6)
                        {
                            testtx = CreateSignedP2PKHTx(wallet_type, bal, curr_add, false, false);
						}
						else if (skip_eth == false)
                        {
                            testeth_tx = CreateSignedEthereumTransaction(wallet_type, curr_add, bal, false, 0, "");
                        }
                    }
                }

                //All Neblio based tokens are the same address as the neblio wallet
                string new_add = "";
                if (testtx != null || testeth_tx != null)
                {
                    new_add = AddNewWalletAddress(wallet_type); //Will give us our new address that we own
                                                                //Then we need to send to this address
                                                                //Now create a real transaction
                    if (wallet_type == 0)
                    { //Neblio
                      //First send all the tokens
                      //There is a specific transaction for neblio tokens + neblio
                        Transaction tx = CreateNTP1AllTokenTransfer(new_add); //This will create a transaction that sends all tokens to another address
                        if (tx != null)
                        {
                            //Broadcast and save the result
                            bool timeout;
                            string txhash = TransactionBroadcast(wallet_type, tx.ToHex(), out timeout);
                            if (txhash.Length > 0)
                            {
                                bal = GetWalletAmount(wallet_type); //Get blockchain balance
                                AddMyTxToDatabase(tx.GetHash().ToString(), curr_add, new_add, bal, 0, 2, UTCTime());
                                UpdateWalletStatus(0, 2); //Wait mode
                            }
                        }
                    }
                    else
                    {
                        bal = GetWalletAmount(wallet_type); //Get blockchain balance
                        if (testtx != null)
                        {
                            Transaction tx = CreateSignedP2PKHTx(wallet_type, bal, new_add, true, false);
                            if (tx != null)
                            {
                                AddMyTxToDatabase(tx.GetHash().ToString(), curr_add, new_add, bal, wallet_type, 2, UTCTime());
                            }
                        }
                        else if (testeth_tx != null)
                        {
							Nethereum.Signer.TransactionChainId eth_tx = CreateSignedEthereumTransaction(wallet_type,new_add, bal, false, 0, "");
                            bool timeout;
                            TransactionBroadcast(wallet_type, eth_tx.Signed_Hex, out timeout);
                            if (timeout == false)
                            {
                                UpdateWalletStatus(wallet_type, 2); //Set to wait
                                AddMyTxToDatabase(eth_tx.HashID, curr_add, new_add, bal, wallet_type, 2, -1); //Withdrawal
                            }
                        }
                    }
                }
            }

            //Now Load the wallet again
            LoadWallet();

            //Refresh View
            if (main_window_loaded == true)
            {
				Application.Invoke(delegate
                {
					//Reload the wallet list in the nodestore
					main_window.Wallet_View_Public.NodeStore.Clear();
					for (int i = 0; i < App.WalletList.Count; i++)
                    {
                        main_window.Wallet_View_Public.NodeStore.AddNode(App.WalletList[i]);
                    }
                });
            }

        }

		public static string AddNewWalletAddress(int wallet)
        {
            //Open the wallet and get all the info
            string new_add = "";
            int blockchain = GetWalletBlockchainType(wallet);
            using (System.IO.StreamReader file_in =
                new System.IO.StreamReader(@App_Path + "/data/account.dat", false))
            {
                using (System.IO.StreamWriter file_out =
                    new System.IO.StreamWriter(@App_Path + "/data/account_new.dat", false))
                {
                    file_in.ReadLine(); //Skip version
                    int enc = Convert.ToInt32(file_in.ReadLine()); //Find out if encrypted

                    file_out.WriteLine(accountdat_version);
                    file_out.WriteLine(enc);
                    for (int i = 0; i < total_cointypes; i++)
                    {
                        string master = file_in.ReadLine(); //Get master key
                        file_out.WriteLine(master);
                        int wtype = Convert.ToInt32(file_in.ReadLine()); //Get the wallet blockchain type
                        int amount = Convert.ToInt32(file_in.ReadLine()); //Get number of subkeys for master
                        file_out.WriteLine(wtype);
                        if (blockchain == wtype)
                        {
                            //This is the wallet we are updating
                            file_out.WriteLine(amount + 1);
                        }
                        else
                        {
                            file_out.WriteLine(amount);
                        }
                        for (int i2 = 1; i2 <= amount; i2++)
                        {
                            //Go through each line and read then write them
                            file_out.WriteLine(file_in.ReadLine());
                            file_out.WriteLine(file_in.ReadLine());
                        }
                        if (blockchain == wtype)
                        {
                            //Now add our new address to the wallet
                            if (enc > 0)
                            {
                                master = AESDecrypt(master, my_wallet_pass);
                            }
                            ExtKey my_new_key = GeneratePrivateKey(master, amount);
                            Network my_net;
                            if (testnet_mode == false)
                            {
                                my_net = Network.Main;
                            }
                            else
                            {
                                my_net = Network.TestNet;
                            }
                            string privatekey = my_new_key.ToString(my_net);
                            string myaddress = GenerateCoinAddress(my_new_key, wallet);
                            if (blockchain == 6)
                            {
                                //Calculate the Ethereum address from the private key
                                myaddress = GenerateEthAddress(privatekey);
                            }
                            new_add = myaddress;
                            if (enc > 0)
                            {
                                privatekey = AESEncrypt(privatekey, my_wallet_pass);
                            }
                            file_out.WriteLine(privatekey);
                            file_out.WriteLine(myaddress);
                        }
                    }
                }
            }

            if (File.Exists(App_Path + "/data/account_new.dat") != false)
            {
                File.Delete(App_Path + "/data/account.dat");
                File.Move(App_Path + "/data/account_new.dat", App_Path + "/data/account.dat");
            }
            return new_add;
        }

        public static void EncryptWalletKeys()
        {
            //Will return a wallet with encrypted keys
            using (System.IO.StreamReader file_in =
                new System.IO.StreamReader(@App_Path + "/data/account.dat", false))
            {
                using (System.IO.StreamWriter file_out =
                    new System.IO.StreamWriter(@App_Path + "/data/account_new.dat", false))
                {
                    file_in.ReadLine(); //Skip version
                    int enc = Convert.ToInt32(file_in.ReadLine()); //Find out if encrypted
                    if (enc > 0) { return; }

                    file_out.WriteLine(accountdat_version);
                    file_out.WriteLine(1);
                    for (int i = 0; i < total_cointypes; i++)
                    {
                        string master = file_in.ReadLine(); //Get master key
                        file_out.WriteLine(AESEncrypt(master, my_wallet_pass));
                        int wtype = Convert.ToInt32(file_in.ReadLine()); //Get the wallet type
                        int amount = Convert.ToInt32(file_in.ReadLine()); //Get number of subkeys for master
                        file_out.WriteLine(wtype);
                        file_out.WriteLine(amount);
                        for (int i2 = 1; i2 <= amount; i2++)
                        {
                            //Go through each line and read then write them
                            string priv_key = file_in.ReadLine();
                            file_out.WriteLine(AESEncrypt(priv_key, my_wallet_pass));
                            file_out.WriteLine(file_in.ReadLine());
                        }
                    }
                }
            }

            if (File.Exists(App_Path + "/data/account_new.dat") != false)
            {
                File.Delete(App_Path + "/data/account.dat");
                File.Move(App_Path + "/data/account_new.dat", App_Path + "/data/account.dat");
            }
        }

        public static void DecryptWalletKeys()
        {
            //Will return a wallet with encrypted keys
            using (System.IO.StreamReader file_in =
                new System.IO.StreamReader(@App_Path + "/data/account.dat", false))
            {
                using (System.IO.StreamWriter file_out =
                    new System.IO.StreamWriter(@App_Path + "/data/account_new.dat", false))
                {
                    file_in.ReadLine(); //Skip version
                    int enc = Convert.ToInt32(file_in.ReadLine()); //Find out if encrypted
                    if (enc == 0) { return; }

                    file_out.WriteLine(accountdat_version); //File version
                    file_out.WriteLine(0);
                    for (int i = 0; i < total_cointypes; i++)
                    {
                        string master = file_in.ReadLine(); //Get master key
                        file_out.WriteLine(AESDecrypt(master, my_wallet_pass));
                        int wtype = Convert.ToInt32(file_in.ReadLine()); //Get the wallet type
                        int amount = Convert.ToInt32(file_in.ReadLine()); //Get number of subkeys for master
                        file_out.WriteLine(wtype);
                        file_out.WriteLine(amount);
                        for (int i2 = 1; i2 <= amount; i2++)
                        {
                            //Go through each line and read then write them
                            string priv_key = file_in.ReadLine();
                            file_out.WriteLine(AESDecrypt(priv_key, my_wallet_pass));
                            file_out.WriteLine(file_in.ReadLine());
                        }
                    }
                }
            }

            if (File.Exists(App_Path + "/data/account_new.dat") != false)
            {
                File.Delete(App_Path + "/data/account.dat");
                File.Move(App_Path + "/data/account_new.dat", App_Path + "/data/account.dat");
            }
        }

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0,
                                                                  DateTimeKind.Utc);

        //Converts UTC seconds to DateTime object
        public static DateTime UTC2DateTime(int utc_seconds)
        {
            return UnixEpoch.AddSeconds(utc_seconds);
        }

        public static int UTCTime()
        {
            //Returns time since epoch
            TimeSpan t = DateTime.UtcNow - UnixEpoch;
            return (int)t.TotalSeconds;
        }

        public static double GetRandomNumber(double minimum, double maximum)
        {
			lock(app_random_gen){
				return (double)app_random_gen.NextDouble() * (maximum - minimum) + minimum;
			}
        }

		public static decimal GetRandomDecimalNumber(decimal minimum, decimal maximum)
        {
            lock (app_random_gen)
            {
                //It returns a decimal that we convert to a decimal and scale
                return Convert.ToDecimal(app_random_gen.NextDouble()) * (maximum - minimum) + minimum;
            }
        }

		public static string GenerateHexNonce(int length)
		{
			//Generates a random hex sequence
			//Not cryptographically secure so not used for key creation

			byte[] buffer = new byte[length / 2];
			lock(app_random_gen){
				app_random_gen.NextBytes(buffer);	
			}
			string[] result = new string[length / 2];
			for (int i = 0; i < buffer.Length; i++)
			{
				result[i] = buffer[i].ToString("X2").ToLower(); //Hex format
			}
			if (length % 2 == 0)
			{ //Even length
				return String.Concat(result); //Returns all the strings together
			}
			//Odd length
			lock(app_random_gen){
				return (String.Concat(result) + app_random_gen.Next(16).ToString("X").ToLower());
			}
		}

        public static bool IsNumber(string s)
        {
            //This will check to see if the string is a valid number
            if (s.Length > 32) { return false; }
            if (s.Trim().Length == 0) { return false; }
            decimal number = 0;
            bool myint = decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out number);
            if (myint == false) { return false; }
            try
            {
                number = decimal.Parse(s, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
      
        public static decimal GetMarketBalance(int market, int type)
        {
            //Helper function to quickly get amount needed for trade type
            int my_wallet = 0;
            if (type == 0)
            {
                //We want to buy NEBL, so we need BTC
                my_wallet = MarketList[market].base_wallet;
            }
            else
            {
                //We want to sell NEBL
                my_wallet = MarketList[market].trade_wallet;
            }

            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == my_wallet)
                {
                    //Find the wallet and return the balance
                    return WalletList[i].balance;
                }
            }
            return 0;
        }

        public static decimal GetWalletAmount(int wallet)
        {
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == wallet)
                {
                    //Find the wallet and return the balance
                    return WalletList[i].balance;
                }
            }
            return 0;
        }

        public static string GetWalletAddress(int wallet)
        {
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == wallet)
                {
                    //Find the wallet and return the balance
                    return WalletList[i].address;
                }
            }
            return "";
        }

        public static string GetWalletTokenID(int wallet)
        {
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == wallet)
                {
                    //Find the wallet and return the ID
                    return WalletList[i].TokenID;
                }
            }
            return "";
        }

		public static string GetWalletERC20TokenContract(int wallet)
        {
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == wallet)
                {
                    //Find the wallet and return the Contract
                    return WalletList[i].ERC20Contract;
                }
            }
            return "";
        }

        public static decimal GetWalletERC20TokenDecimals(int wallet)
        {
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == wallet)
                {
                    //Find the wallet and return the Contract
                    return WalletList[i].ERC20Decimals;
                }
            }
            return 0;
        }

        public static void UpdateWalletStatus(int wallettype, int status)
        {
            for (int i2 = 0; i2 < WalletList.Count; i2++)
            {
                if (WalletList[i2].type == wallettype)
                {
                    WalletList[i2].status = status;

                    if (status != 1)
                    {
                        //Pending only applies to one
                        for (int i3 = 0; i3 < WalletList.Count; i3++)
                        {
                            if (WalletList[i3].address == WalletList[i2].address)
                            {
                                //NTP1 token
                                WalletList[i3].status = WalletList[i2].status; //They share the same status
                            }
                        }
                    }
                    break;
                }
            }
            
        }

        public static void UpdateWalletBalance(int type, decimal balance, decimal ubalance)
        {
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == type)
                {
                    //Find the wallet and change the amount
                    WalletList[i].balance = balance;

                    if (ubalance > 0 && WalletList[i].status == 0)
                    {
                        //There is an unconfirmed amount
                        //Change this to pending
                        WalletList[i].status = 1;
                    }
                    else if (ubalance == 0 && WalletList[i].status == 1)
                    {
                        WalletList[i].status = 0; //Available
                        if (critical_node == true && (type == 3 || type == 0))
                        {
                            RecalculateCNWeight(); //Recalculate the CN weight
                        }
                    }

                    break;
                }
            }

            //Update the wallet amounts
            if (main_window_loaded == true)
            {
				Application.Invoke(delegate
                {
					main_window.Wallet_View_Public.QueueDraw(); //Force redraw
                });
            }
        }

        public static bool CheckWalletBalance(int type, decimal amount, ref string msg)
        {
            //This will check if the amount if spendable by the wallet and give message if not
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == type)
                {
                    if (WalletList[i].status == 2)
                    {
                        //Wallet not available to spend
                        msg = WalletList[i].Coin + " wallet currently unavailable to use.";
                        return false;
                    }
                    if (WalletList[i].balance < amount)
                    {
                        //Trying to spend too much of balance
                        msg = "This amount exceeds " + WalletList[i].Coin + " wallet balance.";
                        return false;
                    }
                    return true;
                }
            }
            return false;
        }

		public static bool CheckMarketFees(int market, int type, decimal amount, ref string msg, bool taker)
        {
            //The wallet must have enough funds to cover the market fees as well
            decimal vn_fee = 0;
            decimal block_fee = 0;
            int sendwallet = 0;
            if (type == 0)
            {
                //We are buying
                if (MarketList[market].trade_wallet == 3)
                {
                    //Ndex
                    vn_fee = 0;
                }
                else
                {
                    vn_fee = ndex_fee / 2;
                }

                sendwallet = MarketList[market].base_wallet;
                int sendwallet_blockchaintype = GetWalletBlockchainType(sendwallet);
                if (sendwallet_blockchaintype == 0)
                {
                    block_fee = blockchain_fee[0] * 4; //Expected amount of Neblio to spend
                }
                else
                {
                    if (sendwallet_blockchaintype != 6)
                    {
                        block_fee = blockchain_fee[sendwallet_blockchaintype]; //Overestimate the fee (average tx is 225 bytes), need more since sending to contract
                    }
                    else
                    {
						block_fee = GetEtherContractTradeFee(Wallet.CoinERC20(sendwallet));
                    }
                }
            }
            else
            {
                //Selling
                if (MarketList[market].trade_wallet == 3)
                {
                    vn_fee = ndex_fee; //We cover the entire fee                    
                }
                else
                {
                    vn_fee = ndex_fee / 2;
                }

                sendwallet = MarketList[market].trade_wallet;
                int sendwallet_blockchaintype = GetWalletBlockchainType(sendwallet);
                if (sendwallet_blockchaintype == 0)
                {
                    block_fee = blockchain_fee[0] * 4; //Expected amount of Neblio to spend
                    int basewallet_blockchaintype = GetWalletBlockchainType(MarketList[market].base_wallet);
                    if (sendwallet == 3 && basewallet_blockchaintype != 0)
                    { //Sending to non-Neblio wallet
                        block_fee = blockchain_fee[0] * 14; //When selling NDEX to those who don't hold NEBL, give them extra 5 trades
                    }
                }
                else
                {
                    if (sendwallet_blockchaintype != 6)
                    {
                        block_fee = blockchain_fee[sendwallet_blockchaintype]; //Overestimate the fee (average tx is 225 bytes), need more since sending to contract
                    }
                    else
                    {
                        //We are sending ethereum to eventual contract, will need at least 265,000 units of gas to cover transfer
						block_fee = GetEtherContractTradeFee(Wallet.CoinERC20(sendwallet));
                    }
                }
            }

            //This is unique to Ethereum, but both the sender and receiver must have a little ethereum to interact with the ethereum contract
            if (GetWalletBlockchainType(MarketList[market].trade_wallet) == 6 || GetWalletBlockchainType(MarketList[market].base_wallet) == 6)
            {
				decimal ether_fee = GetEtherContractRedeemFee(Wallet.CoinERC20(sendwallet));
                if (GetWalletAmount(GetWalletType(6)) < ether_fee)
                {
                    msg = "Your Ether wallet requires a small amount of Ether (" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", ether_fee) + " ETH) to interact with the Swap contract.";
                    return false;
                }
            }

            decimal mybalance = 0;
            bool ntp1_wallet = IsWalletNTP1(sendwallet);
            if (ntp1_wallet == true)
            {
                //NTP1 transactions, only balance that matters is neblio for fees
                mybalance = GetWalletAmount(0);
                if (mybalance < block_fee)
                {
                    //Not enough to pay for fees
                    msg = "This NTP1 token requires a small NEBL balance (" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", block_fee) + " NEBL) to pay for blockchain fees.";
                    return false;
                }
            }
            else
            {
				if (Wallet.CoinERC20(sendwallet) == false)
                {
                    mybalance = GetWalletAmount(sendwallet);
                    if (mybalance - amount < block_fee)
                    {
                        msg = "Your future balance after the trade is not high enough to pay for the blockchain fees.";
                        return false;
                    }
                }
                else
                {
                    //Sending an ERC20
                    //Eth balance needs to be greater than block_fee to send tokens
                    mybalance = GetWalletAmount(17); //ETH wallet
                    if (mybalance < block_fee)
                    {
                        //Not enough to pay for ETH fees
                        msg = "This ERC20 token requires a small ETH balance (" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", block_fee) + " ETH) to pay for blockchain fees.";
                        return false;
                    }
                }
            }

            decimal ndex_balance = GetWalletAmount(3);
            if (sendwallet == 3)
            {
                ndex_balance -= amount; //In case we are sending NDEX
            }
            if (ndex_balance < vn_fee)
            {
                msg = "You do not have enough NDEX to pay for the validation fees for this trade.";
                return false;
            }

            if (vn_fee > 0)
            {
                //Check NDEX wallet availability
                for (int i = 0; i < WalletList.Count; i++)
                {
                    if (WalletList[i].type == 3)
                    { //NDEX wallet
                        if (WalletList[i].status == 2)
                        {
                            msg = "The NDEX wallet is unavailable currently so it cannot pay the fee. Please wait.";
                            return false;
                        }
                        break;
                    }
                }

                //Also make sure that we have enough NEBL for gas for the fee
                mybalance = GetWalletAmount(0);
                if (mybalance < blockchain_fee[0] * 5)
                {
                    //Not enough to pay for fees
                    msg = "This transaction requires a small NEBL balance (" + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", blockchain_fee[0] * 5) + " NEBL) to pay for NDEX blockchain fees.";
                    return false;
                }
            }

            return true;
        }

        public static string GetWalletPubkey(int type)
        {
            //This will return the public key for a wallet
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == type)
                {
                    //Desired wallet
                    lock (transactionLock)
                    {
                        //Prevent more than one thread from using the network
                        Network my_net;
                        if (testnet_mode == false)
                        {
                            my_net = Network.Main;
                        }
                        else
                        {
                            my_net = Network.TestNet;
                        }

                        //Change the network
                        ChangeVersionByte(WalletList[i].type, ref my_net); //NEBL network
                        ExtKey priv_key = ExtKey.Parse(WalletList[i].private_key, my_net);
                        return priv_key.PrivateKey.PubKey.ToString(); //Will transmit the pubkey
                    }
                }
            }
            return "";
        }

		public static int GetWalletBlockchainType(int type)
        {
            //This will return the blockchain type of the selected wallet type
            return Wallet.BlockchainType(type);
        }

        public static bool IsWalletNTP1(int type)
        {
            //This will return true if wallet is of NTP1 type
            return Wallet.CoinNTP1(type);
        }

        public static int GetWalletType(int blockchaintype)
        {
            //Returns the wallet type based on the blockchain
            return Wallet.WalletType(blockchaintype);
        }

        //Check orders, load orders clear orders, remove 1 order, add 1 order, update 1 order
        //This will only store maker orders
        public static bool CheckSavedOrders()
        {
            //This function will check the database if you had open orders and return true if you did
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            statement = new SqliteCommand("Select Count(nindex) From SAVEDORDERS", mycon);
            statement.CommandType = CommandType.Text;
            int saved_ord = Convert.ToInt32(statement.ExecuteScalar().ToString());
            statement.Dispose();
            mycon.Close();
            if (saved_ord > 0)
            { //We have some orders that are not saved
                return true;
            }
            return false;
        }

        public static void LoadSavedOrders()
        {
            //This function will check the database if you had open orders and return true if you did
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            string myquery = "Select type, market, nonce, price, amount, min_amount From SAVEDORDERS";
            statement = new SqliteCommand(myquery, mycon);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            int ctime = UTCTime();
            while (statement_reader.Read())
            {
                //Add each saved order to the order queueu
                int market = Convert.ToInt32(statement_reader["market"]);
                int type = Convert.ToInt32(statement_reader["type"]);
                string nonce = statement_reader["nonce"].ToString();
				decimal price = Decimal.Parse(statement_reader["price"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                decimal amount = Decimal.Parse(statement_reader["amount"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                decimal min_amount = Decimal.Parse(statement_reader["min_amount"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
            
                OpenOrder ord = new App.OpenOrder();
                ord.order_nonce = nonce;
                ord.market = market;
                ord.type = type;
                ord.price = Math.Round(price, 8);
                ord.amount = Math.Round(amount, 8);
                ord.minimum_amount = Math.Round(min_amount, 8);
                ord.original_amount = amount;
                ord.order_stage = 0;
                ord.my_order = true;
                ord.queued_order = true;
                ord.pendtime = ctime;

                //Now add to the queue
                lock (MyOpenOrderList)
                {
                    MyOpenOrderList.Add(ord); //Add to our own personal list
                }
                Application.Invoke(delegate
                {
                    if (App.main_window_loaded == true)
                    {
                        //Must manually add the queued order to the view
                        App.main_window.Open_Order_List_Public.NodeStore.AddNode(ord);
                    }
                });
            }

            statement_reader.Close();
            statement.Dispose();
            mycon.Close();
        }

        public static void ClearSavedOrders()
        {
            //This function will clear the database of savedorders
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            statement = new SqliteCommand("Delete From SAVEDORDERS", mycon);
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
        }

        public static void RemoveSavedOrder(OpenOrder ord)
        {
            //This will delete a specific order from the table that has the same nonce
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            statement = new SqliteCommand("Delete From SAVEDORDERS Where nonce = @non", mycon);
            statement.Parameters.AddWithValue("@non", ord.order_nonce);
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
        }

        public static void UpdateSavedOrder(OpenOrder ord)
        {
            //This will update the amount of a specific order
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            statement = new SqliteCommand("Update SAVEDORDERS Set amount = @val, min_amount = @val2 Where nonce = @non", mycon);
            statement.Parameters.AddWithValue("@non", ord.order_nonce);
            statement.Parameters.AddWithValue("@val", ord.amount.ToString(CultureInfo.InvariantCulture));
            statement.Parameters.AddWithValue("@val2", ord.minimum_amount.ToString(CultureInfo.InvariantCulture));
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
        }

        public static void AddSavedOrder(OpenOrder ord)
        {
            //This will add a specific order to the table
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            statement = new SqliteCommand("Insert Into SAVEDORDERS (type,market,nonce,price,amount,min_amount) Values (@typ,@mark,@non,@pri,@amo,@min_amo);", mycon);
            statement.Parameters.AddWithValue("@typ", ord.type);
            statement.Parameters.AddWithValue("@mark", ord.market);
            statement.Parameters.AddWithValue("@non", ord.order_nonce);
            statement.Parameters.AddWithValue("@pri", ord.price.ToString(CultureInfo.InvariantCulture));
            statement.Parameters.AddWithValue("@amo", ord.amount.ToString(CultureInfo.InvariantCulture));
            statement.Parameters.AddWithValue("@min_amo", ord.minimum_amount.ToString(CultureInfo.InvariantCulture));
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
        }
        
    }
}
