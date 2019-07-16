/*
 * Created by SharpDevelop.
 * User: David
 * Date: 6/21/2019
 * Time: 6:16 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
 
 //NebliDex will now feature an Ethereum perpetual swap contract that can be used to perform atomic swaps
 //It functions very similar to the Bitcoin based blockchains atomic swaps and utilizes some features of Nethereum

using System;
using System.IO;
using System.Windows;
using System.Data;
using System.Numerics;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Policy;
using Nethereum;
using System.Globalization;
using System.Threading.Tasks;
using System.Text;
 
namespace NebliDex_Linux
{
	
	public partial class App
    {

        public static string ETH_ATOMICSWAP_ADDRESS; //This is the address of the publicly viewable atomic swap ethereum contract
		public static string ERC20_ATOMICSWAP_ADDRESS; //This is the address of the publicly viewable atomic swap erc20 contract
		public static long ETH_CALLS = -1; //This will give each query to an ethereum API a specific ID that is not reused

        //Create a list of all the DNSSeeds for all electrum nodes
        public static List<EthereumApiNode> EthereumApiNodeList = new List<EthereumApiNode>();

        public class EthereumApiNode //A list of various nodes that we can connect to get data from the Ethereum blockchain
        {
            public bool testnet;
            public int type;
            public string endpoint;
            public EthereumApiNode(string point, int t, bool test)
            {
                type = t;
                endpoint = point;
                testnet = test;
            }
        }

        public static string GenerateEthAddress(string private_key_string)
        {
            //Uses an extended private key to generate an Eth address
            ExtKey priv_key = null;
            lock (transactionLock)
            { //Prevents other threads from accessing this code at same time
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
                ChangeVersionByte(0, ref my_net); //We will use Neblio's extended private key format
                priv_key = ExtKey.Parse(private_key_string, my_net);
            }
            byte[] private_bytes = priv_key.PrivateKey.ToBytes();
            Nethereum.Signer.EthECKey ethkey = new Nethereum.Signer.EthECKey(private_bytes, true);
            return ethkey.GetPublicAddress();
        }

		public static Decimal GetBlockchainEthereumBalance(string address, int wallet)
        {
			
			//First check if the wallet is an ERC20
            if (Wallet.CoinERC20(wallet) == true)
            {
                //New method to check contract
                return GetERC20Balance(address, wallet);
            }

            //This function returns the amount of ether at an address
            int api_used = (int)Math.Round(GetRandomNumber(1, EthereumApiNodeList.Count)) - 1;
            EthereumApiNode api_node = EthereumApiNodeList[api_used];
            string api_endpoint = api_node.endpoint;

            //Increment the eth calls
            ETH_CALLS++;
            if (ETH_CALLS >= long.MaxValue)
            {
                ETH_CALLS = 0;
            }

            try
            {
                //Each API has a different way to do this
                bool timeout = false;
                if (api_node.type == 0)
                {
                    //Etherscan
                    //Powered by Etherscan.io APIs (etherscan.io)
                    string url = api_endpoint + "?module=account&action=balance&address=" + address + "&tag=latest";
                    string resp = HttpRequest(url, "", out timeout);
                    if (resp.Length == 0)
                    {
                        NebliDexNetLog("Failed to get response from Etherscan");
                        return 0; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["message"].ToString() != "OK")
                    {
                        NebliDexNetLog("Etherscan error: " + resp);
                        return 0;
                    }
                    BigInteger wei_amount = BigInteger.Parse(js["result"].ToString());
                    Decimal ether_amount = ConvertToEther(wei_amount);
                    return ether_amount;
                }
                else if (api_node.type == 1 || api_node.type == 2)
                {
                    //MyEtherWallet/Api or Cloudflare
                    //They both use JSON RPC to interact with Ethereum blockchain and POSTdata              
                    string url = api_endpoint;
                    JObject postdata = new JObject();
                    postdata["jsonrpc"] = "2.0";
                    postdata["method"] = "eth_getBalance";
                    postdata["id"] = ETH_CALLS;
                    JArray param = new JArray(address, "latest");
                    postdata["params"] = param;
                    string serialized_postdata = JsonConvert.SerializeObject(postdata);
                    string resp = HttpRequest(url, serialized_postdata, out timeout);
                    if (resp.Length == 0)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("Failed to get response from MyEtherApi");
                        }
                        else
                        {
                            NebliDexNetLog("Failed to get response from Cloudflare");
                        }
                        return 0; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("MyEtherWallet error: " + resp);
                        }
                        else
                        {
                            NebliDexNetLog("Cloudflare error: " + resp);
                        }
                        return 0;
                    }
                    //RPC returns hex encoded numbers
                    Nethereum.Hex.HexTypes.HexBigInteger hex_wei_amout = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    BigInteger wei_amount = hex_wei_amout.Value;
                    Decimal ether_amount = ConvertToEther(wei_amount);
                    return ether_amount;
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Error querying API " + api_endpoint + " for address: " + address + ", error: " + e.ToString());
            }

            return 0;
        }

        public static long GetBlockchainEthereumAddressNonce(string address)
        {
            //This function returns the nonce from the ethereum address
            //The nonce is used for the next transaction, if -1 returned, error finding nonce
            int api_used = (int)Math.Round(GetRandomNumber(1, EthereumApiNodeList.Count)) - 1;

            //Each API has a different way to do this
            EthereumApiNode api_node = EthereumApiNodeList[api_used];
            string api_endpoint = api_node.endpoint;

            //Increment the eth calls
            ETH_CALLS++;
            if (ETH_CALLS >= long.MaxValue)
            {
                ETH_CALLS = 0;
            }

            try
            {
                bool timeout = false;
                if (api_node.type == 0)
                {
                    //Etherscan
                    string url = api_endpoint + "?module=proxy&action=eth_getTransactionCount&address=" + address + "&tag=latest";
                    string resp = HttpRequest(url, "", out timeout);
                    if (resp.Length == 0)
                    {
                        NebliDexNetLog("Failed to get response from Etherscan");
                        return -1; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        NebliDexNetLog("Etherscan error: " + resp);
                        return -1;
                    }
                    Nethereum.Hex.HexTypes.HexBigInteger nonce = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    return (long)nonce.Value;
                }
                else if (api_node.type == 1 || api_node.type == 2)
                {
                    //MyEtherAPI or Cloudflare
                    //MyEtherWallet/Api or Cloudflare
                    //They both use JSON RPC to interact with Ethereum blockchain and POSTdata              
                    string url = api_endpoint;
                    JObject postdata = new JObject();
                    postdata["jsonrpc"] = "2.0";
                    postdata["method"] = "eth_getTransactionCount";
                    postdata["id"] = ETH_CALLS;
                    JArray param = new JArray(address, "latest");
                    postdata["params"] = param;
                    string serialized_postdata = JsonConvert.SerializeObject(postdata);
                    string resp = HttpRequest(url, serialized_postdata, out timeout);
                    // Add quotation marks around numbers
                    // string pattern = @"(?<![""\w])(\d{1,})(?![""\w])";
                    // resp = System.Text.RegularExpressions.Regex.Replace(resp,pattern,"\"$1\"");
                    // Not used here
                    if (resp.Length == 0)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("Failed to get response from MyEtherApi");
                        }
                        else
                        {
                            NebliDexNetLog("Failed to get response from Cloudflare");
                        }
                        return -1; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("MyEtherWallet error: " + resp);
                        }
                        else
                        {
                            NebliDexNetLog("Cloudflare error: " + resp);
                        }
                        return -1;
                    }
                    //RPC returns hex encoded numbers
                    Nethereum.Hex.HexTypes.HexBigInteger nonce = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    return (long)nonce.Value;
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Error querying API " + api_endpoint + " for address: " + address + ", error: " + e.ToString());
            }
            return -1;
        }

        public static Decimal GetBlockchainEthereumGas()
        {
            //This function returns the gas price in Gwei
            int api_used = (int)Math.Round(GetRandomNumber(1, EthereumApiNodeList.Count)) - 1;

            //Each API has a different way to do this
            EthereumApiNode api_node = EthereumApiNodeList[api_used];
            string api_endpoint = api_node.endpoint;

            //Increment the eth calls
            ETH_CALLS++;
            if (ETH_CALLS >= long.MaxValue)
            {
                ETH_CALLS = 0;
            }

            try
            {
                bool timeout = false;
                if (api_node.type == 0)
                {
                    //Etherscan
                    string url = api_endpoint + "?module=proxy&action=eth_gasPrice";
                    string resp = HttpRequest(url, "", out timeout);
                    if (resp.Length == 0)
                    {
                        NebliDexNetLog("Failed to get response from Etherscan");
                        return -1; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        NebliDexNetLog("Etherscan error: " + resp);
                        return -1;
                    }
                    Nethereum.Hex.HexTypes.HexBigInteger wei_gas = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    return ConvertToGwei(wei_gas);
                }
                else if (api_node.type == 1 || api_node.type == 2)
                {
                    //MyEtherAPI or Cloudflare
                    //MyEtherWallet/Api or Cloudflare
                    //They both use JSON RPC to interact with Ethereum blockchain and POSTdata              
                    string url = api_endpoint;
                    JObject postdata = new JObject();
                    postdata["jsonrpc"] = "2.0";
                    postdata["method"] = "eth_gasPrice";
                    postdata["id"] = ETH_CALLS;
                    JArray param = new JArray();
                    postdata["params"] = param;
                    string serialized_postdata = JsonConvert.SerializeObject(postdata);
                    string resp = HttpRequest(url, serialized_postdata, out timeout);
                    if (resp.Length == 0)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("Failed to get response from MyEtherApi");
                        }
                        else
                        {
                            NebliDexNetLog("Failed to get response from Cloudflare");
                        }
                        return -1; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("MyEtherWallet error: " + resp);
                        }
                        else
                        {
                            NebliDexNetLog("Cloudflare error: " + resp);
                        }
                        return -1;
                    }
                    //RPC returns hex encoded numbers
                    Nethereum.Hex.HexTypes.HexBigInteger wei_gas = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    return ConvertToGwei(wei_gas);
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Error querying API " + api_endpoint + " for gas price, error: " + e.ToString());
            }
            return -1;
        }

        public static string GetBlockchainEthereumContractResult(string contract_add, string data)
        {
            //Returns the result for a function at a specific contract address
            //Data must already be preformatted with function selector and parameters coded
            int api_used = 0;
            api_used = (int)Math.Round(GetRandomNumber(1, EthereumApiNodeList.Count)) - 1;

            //Each API has a different way to do this
            EthereumApiNode api_node = EthereumApiNodeList[api_used];
            string api_endpoint = api_node.endpoint;

            //Increment the eth calls
            ETH_CALLS++;
            if (ETH_CALLS >= long.MaxValue)
            {
                ETH_CALLS = 0;
            }

            try
            {
                bool timeout = false;
                if (api_node.type == 0)
                {
                    //Etherscan
                    string url = api_endpoint + "?module=proxy&action=eth_call&to=" + contract_add + "&data=" + data + "&tag=latest";
                    string resp = HttpRequest(url, "", out timeout);
                    if (resp.Length == 0)
                    {
                        NebliDexNetLog("Failed to get response from Etherscan");
                        return ""; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        NebliDexNetLog("Etherscan error: " + resp);
                        return "";
                    }
                    string data_result = js["result"].ToString();
                    if (data_result.Length < 3)
                    {
                        //Only returned 0x
                        return "";
                    }
                    return js["result"].ToString();
                }
                else if (api_node.type == 1 || api_node.type == 2)
                {
                    //MyEtherAPI or Cloudflare
                    //MyEtherWallet/Api or Cloudflare
                    //They both use JSON RPC to interact with Ethereum blockchain and POSTdata              
                    string url = api_endpoint;
                    JObject postdata = new JObject();
                    postdata["jsonrpc"] = "2.0";
                    postdata["method"] = "eth_call";
                    postdata["id"] = ETH_CALLS;
                    JObject paramdata = new JObject();
                    paramdata["to"] = contract_add;
                    paramdata["data"] = data;
                    JArray param = new JArray(paramdata, "latest");
                    postdata["params"] = param;
                    string serialized_postdata = JsonConvert.SerializeObject(postdata);
                    string resp = HttpRequest(url, serialized_postdata, out timeout);
                    if (resp.Length == 0)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("Failed to get response from MyEtherApi");
                        }
                        else
                        {
                            NebliDexNetLog("Failed to get response from Cloudflare");
                        }
                        return ""; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("MyEtherWallet error: " + resp);
                        }
                        else
                        {
                            NebliDexNetLog("Cloudflare error: " + resp);
                        }
                        return "";
                    }
                    string data_result = js["result"].ToString();
                    if (data_result.Length < 3)
                    {
                        //Only returned 0x
                        return "";
                    }
                    //RPC returns hex encoded numbers
                    return js["result"].ToString();
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Error querying API " + api_endpoint + " for contract result: " + contract_add + ", error: " + e.ToString());
            }
            return "";
        }

        public static BigInteger CalculateBlockchainEthereumTransactionGas(string from_add, string to_add)
        {
            //Returns the gas expected to use for the transaction
            int api_used = 0;
            api_used = (int)Math.Round(GetRandomNumber(1, EthereumApiNodeList.Count)) - 1;
            //Each API has a different way to do this
            EthereumApiNode api_node = EthereumApiNodeList[api_used];
            string api_endpoint = api_node.endpoint;

            //Increment the eth calls
            ETH_CALLS++;
            if (ETH_CALLS >= long.MaxValue)
            {
                ETH_CALLS = 0;
            }

            try
            {
                bool timeout = false;
                if (api_node.type == 0)
                {
                    //Etherscan
                    string url = api_endpoint + "?module=proxy&action=eth_estimateGas&to=" + to_add + "&from=" + from_add + "&tag=latest";
                    string resp = HttpRequest(url, "", out timeout);
                    if (resp.Length == 0)
                    {
                        NebliDexNetLog("Failed to get response from Etherscan");
                        return -1; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        NebliDexNetLog("Etherscan error: " + resp);
                        return -1;
                    }
                    Nethereum.Hex.HexTypes.HexBigInteger gas_units = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    return gas_units.Value;
                }
                else if (api_node.type == 1 || api_node.type == 2)
                {
                    //MyEtherAPI or Cloudflare
                    //MyEtherWallet/Api or Cloudflare
                    //They both use JSON RPC to interact with Ethereum blockchain and POSTdata              
                    string url = api_endpoint;
                    JObject postdata = new JObject();
                    postdata["jsonrpc"] = "2.0";
                    postdata["method"] = "eth_estimateGas";
                    postdata["id"] = ETH_CALLS;
                    JObject paramdata = new JObject();
                    paramdata["to"] = to_add;
                    paramdata["from"] = from_add;
                    JArray param = new JArray(paramdata, "latest");
                    postdata["params"] = param;
                    string serialized_postdata = JsonConvert.SerializeObject(postdata);
                    string resp = HttpRequest(url, serialized_postdata, out timeout);
                    if (resp.Length == 0)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("Failed to get response from MyEtherApi");
                        }
                        else
                        {
                            NebliDexNetLog("Failed to get response from Cloudflare");
                        }
                        return -1; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("MyEtherWallet error: " + resp);
                        }
                        else
                        {
                            NebliDexNetLog("Cloudflare error: " + resp);
                        }
                        return -1;
                    }
                    Nethereum.Hex.HexTypes.HexBigInteger gas_units = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    return gas_units;
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Error querying API " + api_endpoint + " for address: " + to_add + ", error: " + e.ToString());
            }
            return -1;
        }

        public static string EthereumTransactionBroadcast(string rawhex, out bool timeout)
        {
            int api_used = (int)Math.Round(GetRandomNumber(1, EthereumApiNodeList.Count)) - 1;

            //Each API has a different way to do this
            EthereumApiNode api_node = EthereumApiNodeList[api_used];
            string api_endpoint = api_node.endpoint;
            NebliDexNetLog("Broadcasting Ethereum transaction: " + rawhex);

            //Increment the eth calls
            ETH_CALLS++;
            if (ETH_CALLS >= long.MaxValue)
            {
                ETH_CALLS = 0;
            }

            timeout = false;
            try
            {
                if (api_node.type == 0)
                {
                    //Etherscan
                    string url = api_endpoint;
                    string postdata = "module=proxy&action=eth_sendRawTransaction&hex=" + rawhex;
                    string resp = HttpRequest(url, postdata, out timeout);
                    if (resp.Length == 0)
                    {
                        NebliDexNetLog("Failed to get response from Etherscan");
                        return ""; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        NebliDexNetLog("Etherscan error: " + resp);
                        return "";
                    }
                    NebliDexNetLog("Ethereum transaction result on API " + api_node.type + ": " + resp);
                    return js["result"].ToString();
                }
                else if (api_node.type == 1 || api_node.type == 2)
                {
                    //MyEtherAPI or Cloudflare
                    //MyEtherWallet/Api or Cloudflare
                    //They both use JSON RPC to interact with Ethereum blockchain and POSTdata              
                    string url = api_endpoint;
                    JObject postdata = new JObject();
                    postdata["jsonrpc"] = "2.0";
                    postdata["method"] = "eth_sendRawTransaction";
                    postdata["id"] = ETH_CALLS;
                    JArray param = new JArray(rawhex);
                    postdata["params"] = param;
                    string serialized_postdata = JsonConvert.SerializeObject(postdata);
                    string resp = HttpRequest(url, serialized_postdata, out timeout);
                    if (resp.Length == 0)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("Failed to get response from MyEtherApi");
                        }
                        else
                        {
                            NebliDexNetLog("Failed to get response from Cloudflare");
                        }
                        return ""; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("MyEtherWallet error: " + resp);
                        }
                        else
                        {
                            NebliDexNetLog("Cloudflare error: " + resp);
                        }
                        return "";
                    }
                    //RPC returns hex encoded numbers
                    NebliDexNetLog("Ethereum transaction result on API " + api_node.type + ": " + resp);
                    return js["result"].ToString();
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Error querying API " + api_endpoint + " for rawhex: " + rawhex + ", error: " + e.ToString());
            }
            return "";
        }

        public static int EthereumTransactionConfirmations(string txhash)
        {
            int api_used = (int)Math.Round(GetRandomNumber(1, EthereumApiNodeList.Count)) - 1;

            //Each API has a different way to do this
            EthereumApiNode api_node = EthereumApiNodeList[api_used];
            string api_endpoint = api_node.endpoint;

            //Increment the eth calls
            ETH_CALLS++;
            if (ETH_CALLS >= long.MaxValue)
            {
                ETH_CALLS = 0;
            }

            try
            {
                bool timeout = false;
                if (api_node.type == 0)
                {
                    //Etherscan
                    string url = api_endpoint + "?module=proxy&action=eth_getTransactionByHash&txhash=" + txhash;
                    string resp = HttpRequest(url, "", out timeout);
                    if (resp.Length == 0)
                    {
                        NebliDexNetLog("Failed to get response from Etherscan");
                        return -1; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        NebliDexNetLog("Etherscan error: " + resp);
                        return -1;
                    }
                    //Must do this because eth RPC returns null JSON
                    //Transaction doesn't exist
                    string result_string = js["result"].ToString();
                    if (result_string.Length == 0)
                    {
                        return 0;
                    }
                    if (js["result"]["blockNumber"] == null)
                    {
                        return 0; //Transaction not confirmed or not found
                    }
                    Nethereum.Hex.HexTypes.HexBigInteger tx_blocknum = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"]["blockNumber"].ToString());
                    //Search the current block number and compare the difference to get the number for confirmations
                    url = api_endpoint + "?module=proxy&action=eth_blockNumber";
                    resp = HttpRequest(url, "", out timeout);
                    if (resp.Length == 0)
                    {
                        NebliDexNetLog("Failed to get response from Etherscan");
                        return -1; //Unable to get a response
                    }
                    js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        NebliDexNetLog("Etherscan error: " + resp);
                        return -1;
                    }
                    Nethereum.Hex.HexTypes.HexBigInteger eth_blockheight = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    BigInteger confirmations = eth_blockheight.Value - tx_blocknum.Value + 1;
                    return (int)confirmations;
                }
                else if (api_node.type == 1 || api_node.type == 2)
                {
                    //MyEtherAPI or Cloudflare
                    //MyEtherWallet/Api or Cloudflare
                    //They both use JSON RPC to interact with Ethereum blockchain and POSTdata              
                    string url = api_endpoint;
                    JObject postdata = new JObject();
                    postdata["jsonrpc"] = "2.0";
                    postdata["method"] = "eth_getTransactionByHash";
                    postdata["id"] = ETH_CALLS;
                    JArray param = new JArray(txhash);
                    postdata["params"] = param;
                    string serialized_postdata = JsonConvert.SerializeObject(postdata);
                    string resp = HttpRequest(url, serialized_postdata, out timeout);
                    if (resp.Length == 0)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("Failed to get response from MyEtherApi");
                        }
                        else
                        {
                            NebliDexNetLog("Failed to get response from Cloudflare");
                        }
                        return -1; //Unable to get a response
                    }
                    JObject js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("MyEtherWallet error: " + resp);
                        }
                        else
                        {
                            NebliDexNetLog("Cloudflare error: " + resp);
                        }
                        return -1;
                    }
                    //Must do this because eth RPC returns null JSON
                    //Transaction doesn't exist
                    string result_string = js["result"].ToString();
                    if (result_string.Length == 0)
                    {
                        return 0;
                    }
                    if (js["result"]["blockNumber"] == null)
                    {
                        return 0; //Transaction not confirmed or not found
                    }
                    Nethereum.Hex.HexTypes.HexBigInteger tx_blocknum = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"]["blockNumber"].ToString());

                    ETH_CALLS++;
                    if (ETH_CALLS >= long.MaxValue)
                    {
                        ETH_CALLS = 0;
                    }

                    postdata = new JObject();
                    postdata["jsonrpc"] = "2.0";
                    postdata["method"] = "eth_blockNumber";
                    postdata["id"] = ETH_CALLS;
                    param = new JArray();
                    postdata["params"] = param;
                    serialized_postdata = JsonConvert.SerializeObject(postdata);
                    resp = HttpRequest(url, serialized_postdata, out timeout);
                    if (resp.Length == 0)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("Failed to get response from MyEtherApi");
                        }
                        else
                        {
                            NebliDexNetLog("Failed to get response from Cloudflare");
                        }
                        return -1; //Unable to get a response
                    }
                    js = JObject.Parse(resp);
                    if (js["result"] == null)
                    {
                        if (api_node.type == 1)
                        {
                            NebliDexNetLog("MyEtherWallet error: " + resp);
                        }
                        else
                        {
                            NebliDexNetLog("Cloudflare error: " + resp);
                        }
                        return -1;
                    }
                    Nethereum.Hex.HexTypes.HexBigInteger eth_blockheight = new Nethereum.Hex.HexTypes.HexBigInteger(js["result"].ToString());
                    BigInteger confirmations = eth_blockheight.Value - tx_blocknum.Value + 1;
                    return (int)confirmations;
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Error querying API " + api_endpoint + " for transaction hash: " + txhash + ", error: " + e.ToString());
            }
            return -1;
        }

		public static Nethereum.Signer.TransactionChainId CreateSignedEthereumTransaction(int wallet, string to_address, decimal amount, bool exactamount, int txtype, string contract_data)
        {
            // For Etheruem, unlike Bitcoin, even pulling ether from a contract requires the sender to pay gas fees from their private key account
            // This is done in case the transaction fails, the miner will still get the gas fee from the transaction
            // to_address can be the atomic swap contract
            // predicted gas fee is subtracted from account balance unless account balance is too small, otherwise predicted gas fee is subtracted from sending amount
            // if exactamount is true, transaction will fail and not attempt broadcast
            // there are 5 types of txtypes:
            // 0 - Balance transfer from one ethereum account to another, standard ethereum transfer gas fee
            // 1 - Sending funds to the atomic swap ethereum contract, will increase gas fee to prepare for the eventual transfer out (refund the receiver for gas)
            // 2 - Redeem from the atomic swap ethereum contract, gas fee will be what is required to redeem from contract, essentially paying back redeemer for gas he/she spent
            // 3 - Refund from the atomic swap ethereum contract, same as above
            // 4 - ERC20 approval to contract
            // contract_data includes preformated data that will go to the EVM

            decimal totalamount = 0;
            decimal change = 0;

            totalamount = GetWalletAmount(wallet); //Ether's blockchain type
            change = totalamount - amount; //The ether change after the expected transfer

            if (change < 0) { return null; } //Not enough balance to cover the transfer amount
            try
            {
                //Ethereum is a lot simpler than Bitcoin in terms of address balance as it doesn't require knowledge of UTXOs
                string my_extended_private_key_string = "";
                string my_address = "";
                for (int i = 0; i < WalletList.Count; i++)
                {
                    if (WalletList[i].type == wallet)
                    {
                        my_address = WalletList[i].address;
                        my_extended_private_key_string = WalletList[i].private_key; //Get the ethereum wallet private key
                        break;
                    }
                }

                if (my_extended_private_key_string == "") { return null; } //No private key found

                decimal blockchain_balance = GetBlockchainEthereumBalance(my_address, wallet);
                blockchain_balance = TruncateDecimal(blockchain_balance, 8); //Match wallet balance to eight decimal places
                if (blockchain_balance.Equals(totalamount) == false)
                {
                    NebliDexNetLog("Blockchain amount doesn't match account total.");
                    return null;
                }

                bool is_erc20_token = Wallet.CoinERC20(wallet); //ERC20 tokens have a different set of rules

                //Now we need to calculate the gas that will be used for the transaction
                BigInteger wei_gas_limit = 21000; //This is the base gas in wei (and the only one used for an ethereum normal transaction)
                BigInteger wei_gas_limit_extra = 0; //The wei goes to the contract as extra ether 

                if (is_erc20_token == false)
                {
                    if (txtype == 1)
                    {
                        //Gas used to deposit into contract is around 154,509 units
                        //Sending to atomic swap smart contract, need more gas
                        wei_gas_limit = 160000;
                        wei_gas_limit_extra = 112000; //We will send extra ether into contract to cover eventual transfer out
                    }
                    else if (txtype == 2)
                    {
                        //Gas used to redeem from contract is around 107,505        
                        //Redeeming from atomic swap smart contract
                        wei_gas_limit = 112000; //This should be refunded by the smart contract balance
                        amount = 0;
                    }
                    else if (txtype == 3)
                    {
                        //Gas used to refund from contract is around 40,641 units
                        //Refunding from atomic swap smart contract
                        wei_gas_limit = 45000;
                        amount = 0;
                    }
                    else if (txtype == 0)
                    {
                        //We will calculate the predicted gas used in case sending to a contract
                        //API will return units of gas that transaction may use
                        BigInteger suggested_gas_limit = CalculateBlockchainEthereumTransactionGas(my_address, to_address);
                        if (suggested_gas_limit > wei_gas_limit)
                        {
                            wei_gas_limit = suggested_gas_limit; //Modify our default gas limit for the transer
                        }
                    }
                }
                else
                {
                    //Rules are different when sending tokens, both users will need the sufficient ETH to move around                   
                    //And there is no reimbursement
                    if (txtype == 0)
                    {
                        //This is a straight ERC20 transfer to another address
                        wei_gas_limit = 21000 * 4; //84,000 to cover most cases
                    }
                    else if (txtype == 1)
                    {
                        //Gas used to deposit into contract is around 242,248 units (REP testnet)
                        //Sending to atomic swap smart contract, need more gas
                        wei_gas_limit = 160000 * 2; //ERC20 tokens require more gas
                    }
                    else if (txtype == 2)
                    {
                        //Gas used to redeem from contract is around 137,702 units (REP testnet)
                        //Redeeming from atomic swap smart contract
                        wei_gas_limit = 112000 * 2; //This should be refunded by the smart contract balance
                    }
                    else if (txtype == 3)
                    {
                        //Gas used to refund from contract varies significantly (REP testnet)
                        //Refunding from atomic swap smart contract
                        wei_gas_limit = 112000 * 2;
                    }
                    else if (txtype == 4)
                    {
                        //Approval transaction (can vary based on chain but only 50,000 required for REP testnet)
                        wei_gas_limit = 21000 * 4;
                    }
                    amount = 0; //The token amount is encoded in the data field
                    change = GetWalletAmount(17); //Change now represents the ETH amount since its used for gas
                }

                //Multiply the gas_limit * gas_price to get amount of ether used for gas for this transaction
                decimal dec_wei_gasprice = Decimal.Multiply(blockchain_fee[6], 1000000000m); //This will give us the gas price in wei
                BigInteger wei_gas_price = new BigInteger(dec_wei_gasprice); //Convert the wei gasprice to BigInteger
                BigInteger wei_gas = BigInteger.Multiply(wei_gas_price, wei_gas_limit); //Multiply the price by the limit to get the gas used in wei
                BigInteger wei_gas_extra = BigInteger.Multiply(wei_gas_price, wei_gas_limit_extra);
                decimal ether_gas = ConvertToEther(wei_gas); //Get the gas used in ether
                decimal ether_gas_extra = 0;
                if (wei_gas_extra > 0)
                {
                    //We are reimbursing the redeemer, required by protocol
                    ether_gas_extra = ConvertToEther(wei_gas_extra);
                    amount += ether_gas_extra;
                    change -= ether_gas_extra;
                }

                change -= ether_gas; //Take the gas from the change

                if (change < 0)
                {
                    //Not enough balance to cover the gas
                    if (exactamount == true)
                    {
                        // If we require an exactamount, such as sending to a contract, we must send an exact amount to sender otherwise fail
                        NebliDexNetLog("Not enough Ether to cover exact amount");
                        return null;
                    }

                    //change was not big enough to handle gas fee, so now instead, take from amount sent
                    amount = amount + change; //Subtract from amount, the negative amount of change
                    change = 0;
                    if (amount < 0)
                    {
                        NebliDexNetLog("Not enough funds to pay for transaction.");
                        return null;
                    } //Amount was too small
                }

                //Now calculate the amount to send in Wei
                BigInteger wei_amount = ConvertToWei(amount);

                //Get the address nonce for this user
                BigInteger my_nonce = new BigInteger(GetBlockchainEthereumAddressNonce(my_address));
                if (my_nonce < 0)
                {
                    NebliDexNetLog("Error retrieving ethereum nonce from blockchain");
                    return null;
                }

                //Create the EthPrivateKey
                ExtKey priv_key = null;
                BigInteger transaction_chain = 0;
                lock (transactionLock)
                { //Prevents other threads from accessing this code at same time
                    Network my_net;
                    if (testnet_mode == false)
                    {
                        my_net = Network.Main;
                        transaction_chain = (BigInteger)Convert.ToInt32(Nethereum.Signer.Chain.MainNet);
                    }
                    else
                    {
                        my_net = Network.TestNet;
                        //We use Rinkeby testnet
                        transaction_chain = (BigInteger)Convert.ToInt32(Nethereum.Signer.Chain.Rinkeby);
                    }

                    //Change the network
                    ChangeVersionByte(wallet, ref my_net);
                    priv_key = ExtKey.Parse(my_extended_private_key_string, my_net);
                }
                Nethereum.Signer.EthECKey ethkey = new Nethereum.Signer.EthECKey(priv_key.PrivateKey.ToBytes(), true);

                //Create the etheruem transaction                                         
                Nethereum.Signer.TransactionChainId transaction = null;
                if (contract_data.Length > 0)
                {
                    transaction = new Nethereum.Signer.TransactionChainId(to_address, wei_amount, my_nonce, wei_gas_price, wei_gas_limit, contract_data, transaction_chain);
                }
                else
                {
                    transaction = new Nethereum.Signer.TransactionChainId(to_address, wei_amount, my_nonce, wei_gas_price, wei_gas_limit, transaction_chain);
                }

                //Now sign the transaction

                //We sign with a ChainID to help prevent replay
                transaction.Sign(ethkey);

                return transaction;

            }
            catch (Exception e)
            {
                NebliDexNetLog("Error while creating transaction: " + e.ToString());
            }

            return null;

        }

        public static bool VerifyBlockchainEthereumAtomicSwap(string secrethash_hex, decimal expected_balance, string my_address, int unlocktime_int)
        {
            //We will use the secrethash_hex as an index to recover the expected balance, the redeemer address and the unlocktime_int
            //We will also check to make sure that there is extra ether in the contract to pay for the redeemer's gas cost
            //The redeemer can be the taker on the maker
            BigInteger wei_gas_limit = 110000; //The expected gas_used for a redeem transaction
            BigInteger unlocktime = new BigInteger(unlocktime_int);
            decimal gas_price = blockchain_fee[6] - 5m;
            if (gas_price < 0) { gas_price = 0; } //Must do this since clients may have significant gas price differences
            decimal dec_wei_gasprice = Decimal.Multiply(gas_price, 1000000000m);
            BigInteger wei_gas_price = new BigInteger(dec_wei_gasprice); //Convert the wei gasprice to BigInteger
            BigInteger wei_gas = BigInteger.Multiply(wei_gas_price, wei_gas_limit);

            BigInteger wei_expected_balance = ConvertToWei(expected_balance);
            wei_expected_balance += wei_gas;
            //The balance in the contract must be equal to our exceed this expected balance

            string contract_to_call = ETH_ATOMICSWAP_ADDRESS;
            string function_to_call = "check(bytes32)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);
            string data_string = "0x" + function_selector + encoded_data;
            string result = GetBlockchainEthereumContractResult(contract_to_call, data_string);
            if (result.Length == 0)
            {
                return false; //Contract execution error, cannot verify result
            }
            //Now we need to decode the result
            //No complicated fields like bytes or arrays, so can split string
            //Returns be(uint256 timelock, uint256 value, address withdrawTrader, bytes32 secretLock)
            result = result.Substring(2); //Remove the 0x
            string timelock_hex = result.Substring(0, 64);
            string wei_value_hex = result.Substring(64, 64);
            string address_hex = result.Substring(64 * 2, 64).ToLower();
            Nethereum.Hex.HexTypes.HexBigInteger swap_timelock = new Nethereum.Hex.HexTypes.HexBigInteger(timelock_hex);
            Nethereum.Hex.HexTypes.HexBigInteger swap_value = new Nethereum.Hex.HexTypes.HexBigInteger(wei_value_hex);
            my_address = my_address.Substring(2); //Remove the 0x
            my_address = my_address.ToLower().PadLeft(64, '0'); //Used to match the format returned from the call function
            if (swap_timelock.Value == 0)
            {
                return false; //No balance at the contract yet
            }
            if (swap_timelock.Value != unlocktime)
            {
                NebliDexNetLog("Swap contract timelock doesn't match what is expected");
                return false;
            }
            if (swap_value.Value < wei_expected_balance)
            {
                NebliDexNetLog("Swap contract balance doesn't match what is expected");
                return false;
            }
            if (address_hex.Equals(my_address) == false)
            {
                NebliDexNetLog("Swap contract address doesn't match my address");
                return false;
            }

            return true;
        }

        public static decimal GetBlockchainEthereumAtomicSwapBalance(string secrethash_hex)
        {
            //We will use the secrethash_hex as an index to recover the balance
            string contract_to_call = ETH_ATOMICSWAP_ADDRESS;
            string function_to_call = "check(bytes32)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);
            string data_string = "0x" + function_selector + encoded_data;
            string result = GetBlockchainEthereumContractResult(contract_to_call, data_string);
            if (result.Length == 0)
            {
                return 0; //Contract execution error, cannot verify result
            }
            //Now we need to decode the result
            //No complicated fields like bytes or arrays, so can split string
            //Returns (uint256 timelock, uint256 value, address withdrawTrader, bytes32 secretLock)
            result = result.Substring(2); //Remove the 0x
            string wei_value_hex = result.Substring(64, 64);
            Nethereum.Hex.HexTypes.HexBigInteger swap_value = new Nethereum.Hex.HexTypes.HexBigInteger(wei_value_hex);
            return ConvertToEther(swap_value.Value);
        }

		public static string GetEthereumAtomicSwapSecret(string secrethash_hex, bool erc20)
        {
            //We will use the secrethash_hex as an index to recover the secret hex
            //This will return the secret when the trader has redeemed from the contract

            string contract_to_call = ETH_ATOMICSWAP_ADDRESS;
			if (erc20 == true)
            {
                contract_to_call = ERC20_ATOMICSWAP_ADDRESS;
            }
            string function_to_call = "checkSecretKey(bytes32)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);
            string data_string = "0x" + function_selector + encoded_data;
            string result = GetBlockchainEthereumContractResult(contract_to_call, data_string);
            if (result.Length == 0)
            {
                return ""; //Contract execution error, cannot verify result
            }
            //Now we need to decode the result
            //Returns bytes secretKey
            result = result.Substring(2); //Remove the 0x
            string secret_hex = result.Substring(64 * 2, 66); //Get the 33 bytes from the result, this is the secret

            return secret_hex;
        }

        public static string GenerateEthereumAtomicSwapRedeemData(string secrethash_hex, string secret_hex)
        {
            //This function returns the data used to redeem from the ethereum smart contract

            string function_to_call = "redeem(bytes32,bytes)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "bytes";
            data["data_length"] = Convert.ToInt32(secret_hex.Length / 2); //We need data length since it is a dynamic sized byte array 
            data["data_hex"] = secret_hex;
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);

            return function_selector + encoded_data;
        }

        public static string GenerateEthereumAtomicSwapRefundData(string secrethash_hex)
        {
            //This function returns the data used to refund from the ethereum smart contract
            string function_to_call = "refund(bytes32)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);

            return function_selector + encoded_data;
        }

        public static string GenerateEthereumAtomicSwapOpenData(string secrethash_hex, string to_address, long timelock)
        {
            //This function returns the data used to open an atomic swap from the ethereum smart contract           
            string function_to_call = "open(bytes32,address,uint256)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "address";
            data["data_hex"] = to_address.Substring(2);
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "uint256";
            //Convert the long to hexidecimal bigendian
            BigInteger timelock_bigint = (BigInteger)timelock;
            Nethereum.Hex.HexTypes.HexBigInteger swap_timelock = new Nethereum.Hex.HexTypes.HexBigInteger(timelock_bigint);
            data["data_hex"] = swap_timelock.HexValue.Substring(2); //Omit the 0x
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);

            return function_selector + encoded_data;
        }

		//ERC20 methods
        public static decimal GetERC20Balance(string my_address, int wallet)
        {
            //Send an ETH call to the token contract to pull the balance at the token contract
            string tok_contract = GetWalletERC20TokenContract(wallet);
            if (tok_contract.Length == 0) { return 0; } //No token contract, no balance
            string function_to_call = "balanceOf(address)"; //ERC20 check balance standard
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "address";
            data["data_hex"] = my_address.Substring(2);
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);
            string data_string = "0x" + function_selector + encoded_data;
            string result = GetBlockchainEthereumContractResult(tok_contract, data_string);
            if (result.Length == 0)
            {
                return 0; //Contract execution error, cannot verify result
            }
            //Now we need to decode the result
            //No complicated fields like bytes or arrays, so can split string
            //Returns (uint256 balance)
            result = result.Substring(2); //Remove the 0x
            string wei_value_hex = result.Substring(0, 64);
            Nethereum.Hex.HexTypes.HexBigInteger swap_value = new Nethereum.Hex.HexTypes.HexBigInteger(wei_value_hex);
            //Now based on the token amount of decimals, convert to whole tokens
            if (swap_value.Value == 0) { return 0; }
            return ConvertToERC20Decimal(swap_value.Value, GetWalletERC20TokenDecimals(wallet));
        }

        public static decimal GetERC20AtomicSwapAllowance(string my_address, string contract_address, int wallet)
        {
            //This function will verify the allowance is acceptable to send the amount specified to the atomic sawp contract
            //If allowance is smaller than amount, then suggested to set allowance as 1 million tokens or amount if greater

            //Send an ETH call to the token contract to pull the balance at the token contract
            string tok_contract = GetWalletERC20TokenContract(wallet);
            if (tok_contract.Length == 0) { return -1; } //No token contract, no balance
            string function_to_call = "allowance(address,address)"; //ERC20 check allowance
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "address";
            data["data_hex"] = my_address.Substring(2);
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "address";
            data["data_hex"] = contract_address.Substring(2);
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);
            string data_string = "0x" + function_selector + encoded_data;
            string result = GetBlockchainEthereumContractResult(tok_contract, data_string);
            if (result.Length == 0)
            {
                return -1; //Contract execution error, cannot verify result
            }
            //Now we need to decode the result
            //No complicated fields like bytes or arrays, so can split string
            //Returns (uint256 allowance)
            result = result.Substring(2); //Remove the 0x
            string wei_value_hex = result.Substring(0, 64);
            Nethereum.Hex.HexTypes.HexBigInteger swap_value = new Nethereum.Hex.HexTypes.HexBigInteger(wei_value_hex);
            //Now based on the token amount of decimals, convert to whole tokens
            if (swap_value.Value == 0) { return 0; }
            return ConvertToERC20Decimal(swap_value.Value, GetWalletERC20TokenDecimals(wallet));
        }

        public static string GenerateEthereumERC20TransferData(string to_address, BigInteger intamount)
        {
            //This function returns data to transfer token around
            string function_to_call = "transfer(address,uint256)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "address";
            data["data_hex"] = to_address.Substring(2);
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "uint256";
            Nethereum.Hex.HexTypes.HexBigInteger hex_intamount = new Nethereum.Hex.HexTypes.HexBigInteger(intamount);
            data["data_hex"] = hex_intamount.HexValue.Substring(2); //Omit the 0x
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);

            return function_selector + encoded_data;
        }

        public static string GenerateEthereumERC20ApproveData(string to_address, BigInteger intamount)
        {
            //This function returns data to approve a certain amount for the transferfrom call
            string function_to_call = "approve(address,uint256)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "address";
            data["data_hex"] = to_address.Substring(2);
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "uint256";
            Nethereum.Hex.HexTypes.HexBigInteger hex_intamount = new Nethereum.Hex.HexTypes.HexBigInteger(intamount);
            data["data_hex"] = hex_intamount.HexValue.Substring(2); //Omit the 0x
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);

            return function_selector + encoded_data;
        }

        public static void CreateAndBroadcastERC20Approval(int wallet, decimal amount, string contract_add)
        {
            //This method will create a transaction to the ERC20 contract for approval to spend
            string tok_contract = GetWalletERC20TokenContract(wallet);
            if (tok_contract.Length == 0) { return; } //No token contract, can't verify
            BigInteger intamount = ConvertToERC20Int(amount, GetWalletERC20TokenDecimals(wallet));
            string approve_data = App.GenerateEthereumERC20ApproveData(contract_add, intamount);
            Nethereum.Signer.TransactionChainId tx = CreateSignedEthereumTransaction(wallet, tok_contract, 0, false, 4, approve_data); //Not actually sending anything
            if (tx != null)
            {
                //Broadcast this transaction, and write to log regardless of whether it returns a hash or not
                //Now write to the transaction log
                bool timeout;
                TransactionBroadcast(wallet, tx.Signed_Hex, out timeout);
                if (timeout == false)
                {
                    UpdateWalletStatus(wallet, 2); //Set to wait
                    AddMyTxToDatabase(tx.HashID, GetWalletAddress(wallet), tok_contract, 0, wallet, 2, -1);
                }
                else
                {
                    NebliDexNetLog("Transaction broadcast timed out, not connected to internet");
                }
            }
        }

        public static string GenerateERC20AtomicSwapOpenData(string secrethash_hex, BigInteger intamount, string erc20_contract, string to_address, long timelock)
        {
            //This function returns the data used to open an atomic swap from the ethereum smart contract           
            string function_to_call = "open(bytes32,uint256,address,address,uint256)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "uint256";
            Nethereum.Hex.HexTypes.HexBigInteger hex_intamount = new Nethereum.Hex.HexTypes.HexBigInteger(intamount);
            data["data_hex"] = hex_intamount.HexValue.Substring(2); //Omit the 0x
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "address";
            data["data_hex"] = erc20_contract.Substring(2);
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "address";
            data["data_hex"] = to_address.Substring(2);
            data_array.Add(data);
            data = new JObject();
            data["data_type"] = "uint256";
            //Convert the long to hexidecimal bigendian
            BigInteger timelock_bigint = (BigInteger)timelock;
            Nethereum.Hex.HexTypes.HexBigInteger swap_timelock = new Nethereum.Hex.HexTypes.HexBigInteger(timelock_bigint);
            data["data_hex"] = swap_timelock.HexValue.Substring(2); //Omit the 0x
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);

            return function_selector + encoded_data;
        }

        public static decimal GetERC20AtomicSwapBalance(string secrethash_hex, int wallet)
        {
            //We will use the secrethash_hex as an index to recover the balance
            string contract_to_call = ERC20_ATOMICSWAP_ADDRESS;
            string function_to_call = "check(bytes32)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);
            string data_string = "0x" + function_selector + encoded_data;
            string result = GetBlockchainEthereumContractResult(contract_to_call, data_string);
            if (result.Length == 0)
            {
                return 0; //Contract execution error, cannot verify result
            }
            //Now we need to decode the result
            //No complicated fields like bytes or arrays, so can split string
            //Returns (uint256 timelock, uint256 erc20Value, address erc20ContractAddress, address withdrawTrader, bytes32 secretLock)
            result = result.Substring(2); //Remove the 0x
            string int_value_hex = result.Substring(64, 64);
            Nethereum.Hex.HexTypes.HexBigInteger swap_value = new Nethereum.Hex.HexTypes.HexBigInteger(int_value_hex);
            return ConvertToERC20Decimal(swap_value.Value, GetWalletERC20TokenDecimals(wallet));
        }

        public static bool VerifyERC20AtomicSwap(string secrethash_hex, decimal expected_balance, string my_address, int unlocktime_int, int wallet)
        {
            //We will use the secrethash_hex as an index to recover the expected balance, the redeemer address and the unlocktime_int
            string tok_contract = GetWalletERC20TokenContract(wallet);
            if (tok_contract.Length == 0) { return false; } //No token contract, can't verify
            BigInteger unlocktime = new BigInteger(unlocktime_int);

            BigInteger int_expected_balance = ConvertToERC20Int(expected_balance, GetWalletERC20TokenDecimals(wallet));
            //The balance in the contract must be equal to our exceed this expected balance

            string contract_to_call = ERC20_ATOMICSWAP_ADDRESS;
            string function_to_call = "check(bytes32)";
            string function_selector = GetEthereumFunctionSelectorHex(function_to_call); //Will hash and get the four bytes
                                                                                         //Now encode the data for the call
            JArray data_array = new JArray();
            JObject data = new JObject();
            data["data_type"] = "bytes32";
            data["data_hex"] = secrethash_hex;
            data_array.Add(data);
            string encoded_data = GenerateEthereumDataParams(data_array);
            string data_string = "0x" + function_selector + encoded_data;
            string result = GetBlockchainEthereumContractResult(contract_to_call, data_string);
            if (result.Length == 0)
            {
                return false; //Contract execution error, cannot verify result
            }
            //Now we need to decode the result
            //No complicated fields like bytes or arrays, so can split string
            //Returns (uint256 timelock, uint256 erc20Value, address erc20ContractAddress, address withdrawTrader, bytes32 secretLock)
            result = result.Substring(2); //Remove the 0x
            string timelock_hex = result.Substring(0, 64);
            string int_value_hex = result.Substring(64, 64);
            string contract_address_hex = result.Substring(64 * 2, 64).ToLower();
            string address_hex = result.Substring(64 * 3, 64).ToLower();
            Nethereum.Hex.HexTypes.HexBigInteger swap_timelock = new Nethereum.Hex.HexTypes.HexBigInteger(timelock_hex);
            Nethereum.Hex.HexTypes.HexBigInteger swap_value = new Nethereum.Hex.HexTypes.HexBigInteger(int_value_hex);
            tok_contract = tok_contract.Substring(2); //Remove the 0x
            tok_contract = tok_contract.ToLower().PadLeft(64, '0'); //Used to match the format returned from the call function          
            my_address = my_address.Substring(2); //Remove the 0x
            my_address = my_address.ToLower().PadLeft(64, '0'); //Used to match the format returned from the call function
            if (swap_timelock.Value == 0)
            {
                return false; //No balance at the contract yet
            }
            if (swap_timelock.Value != unlocktime)
            {
                NebliDexNetLog("Swap contract timelock doesn't match what is expected");
                return false;
            }
            if (swap_value.Value < int_expected_balance)
            {
                NebliDexNetLog("Swap contract balance doesn't match what is expected");
                return false;
            }
            if (contract_address_hex.Equals(tok_contract) == false)
            {
                NebliDexNetLog("Swap token address doesn't match my token address");
                return false;
            }
            if (address_hex.Equals(my_address) == false)
            {
                NebliDexNetLog("Swap contract address doesn't match my address");
                return false;
            }

            return true;
        }

        //Helper functions
        public static void GetEthereumBlockchainFee()
        {
            Decimal gas_price = GetBlockchainEthereumGas();
            if (gas_price > 0)
            {
                Decimal gas_price_diff = gas_price - blockchain_fee[6];
                blockchain_fee[6] = Math.Round(blockchain_fee[6] + gas_price_diff / 5m, 2); //Modify the gas prices by small amounts
            }
        }

        public static void GetEthereumWalletBalances()
        {
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].blockchaintype == 6)
                {
                    //This is an Eth Wallet, get the balance
					Decimal bal = GetBlockchainEthereumBalance(WalletList[i].address, WalletList[i].type);
                    if (bal >= 0)
                    {
                        WalletList[i].balance = TruncateDecimal(bal, 8); //We only want to see up to 8 decimal places
                    }
                }
            }
        }

		public static Decimal GetEtherContractTradeFee(bool erc20)
        {
            //Takes the Gwei price per gas unit to calculate the trade fee in ether
            //We are sending ethereum to eventual contract, will need at least 272,000 units of gas to cover transfer                       
            decimal gwei = Decimal.Multiply(blockchain_fee[6], 272000m);
            if (erc20 == true)
            {
                gwei = Decimal.Multiply(blockchain_fee[6], 320000m);
            }
            return ConvertGweiToEther(gwei);
        }

        public static Decimal GetEtherWithdrawalFee(bool erc20)
        {
            //Takes the Gwei price per gas unit to calculate the trade fee in ether
            //The fee required to transfer eth between parties (such as withdrawing)                        
            decimal gwei = Decimal.Multiply(blockchain_fee[6], 21000m);
            if (erc20 == true) { gwei = gwei * 4; }
            return ConvertGweiToEther(gwei);
        }

        public static Decimal GetEtherContractRedeemFee(bool erc20)
        {
            //Takes the Gwei price per gas unit to calculate the trade fee in ether
            //The fee required to redeem from the smart contract, all traders of eth need at least this             
            decimal gwei = Decimal.Multiply(blockchain_fee[6], 112000m);
            if (erc20 == true) { gwei = gwei * 2; }
            return ConvertGweiToEther(gwei);
        }

        public static Decimal ConvertToEther(BigInteger wei)
        {
            BigInteger remainder;
            BigInteger whole_eth_int = BigInteger.DivRem(wei, 1000000000000000000, out remainder);
            Decimal base_eth = (decimal)whole_eth_int;
            Decimal dec_remainder = (decimal)remainder; //This will be less than a quintillion
            dec_remainder = Decimal.Divide(dec_remainder, 1000000000000000000);
            base_eth = Decimal.Add(base_eth, dec_remainder);
            return base_eth;
        }

        public static Decimal ConvertToERC20Decimal(BigInteger wei, decimal decimal_places)
        {
            BigInteger remainder;
            BigInteger divide_factor = new BigInteger(Math.Pow(10, Convert.ToDouble(decimal_places)));
            BigInteger whole_erc20_int = BigInteger.DivRem(wei, divide_factor, out remainder);
            Decimal base_erc20 = (decimal)whole_erc20_int;
            Decimal dec_remainder = (decimal)remainder;
            dec_remainder = Decimal.Divide(dec_remainder, (decimal)divide_factor);
            base_erc20 = Decimal.Add(base_erc20, dec_remainder);
            return base_erc20;
        }

        public static BigInteger ConvertToERC20Int(Decimal erc20, decimal decimal_places)
        {
            BigInteger multiple_factor = new BigInteger(Math.Pow(10, Convert.ToDouble(decimal_places)));
            Decimal left_of_decimal = Math.Floor(erc20);
            Decimal right_of_decimal = erc20 - left_of_decimal;
            Decimal int_right_of_decimal = Decimal.Multiply(right_of_decimal, (decimal)multiple_factor); //Convert to int
            BigInteger int_value = new BigInteger(left_of_decimal);
            BigInteger int_decimal = new BigInteger(int_right_of_decimal);
            int_value = BigInteger.Multiply(int_value, multiple_factor);
            int_value = BigInteger.Add(int_value, int_decimal);
            return int_value;
        }

        public static Decimal ConvertGweiToEther(Decimal gwei)
        {
            Decimal base_eth = Decimal.Divide(gwei, 1000000000m); //This will be our base eth
            return base_eth;
        }

        public static Decimal ConvertToGwei(BigInteger wei)
        {
            BigInteger remainder;
            BigInteger whole_gwei_int = BigInteger.DivRem(wei, 1000000000, out remainder);
            Decimal base_gwei = (decimal)whole_gwei_int;
            Decimal dec_remainder = (decimal)remainder;
            dec_remainder = Decimal.Divide(dec_remainder, 1000000000);
            base_gwei = Decimal.Add(base_gwei, dec_remainder);
            return base_gwei;
        }

        public static BigInteger ConvertToWei(Decimal ether)
        {
            BigInteger multiple_factor = new BigInteger(1000000000000000000); //1 ether = 1 quintillion of wei
            Decimal left_of_decimal = Math.Floor(ether);
            Decimal right_of_decimal = ether - left_of_decimal;
            Decimal wei_right_of_decimal = Decimal.Multiply(right_of_decimal, 1000000000000000000m); //Convert to Wei
            BigInteger wei_value = new BigInteger(left_of_decimal);
            BigInteger wei_decimal = new BigInteger(wei_right_of_decimal);
            wei_value = BigInteger.Multiply(wei_value, multiple_factor);
            wei_value = BigInteger.Add(wei_value, wei_decimal);
            return wei_value;
        }

        public static decimal TruncateDecimal(decimal original, int decimalPlaces)
        {
            decimal integralValue = Math.Truncate(original);

            decimal fraction = original - integralValue;

            decimal factor = (decimal)Math.Pow(10, decimalPlaces);

            decimal truncatedFraction = Math.Truncate(fraction * factor) / factor;

            decimal result = integralValue + truncatedFraction;

            return result;
        }

        public static string GetEthereumFunctionSelectorHex(string function)
        {
            Nethereum.Util.Sha3Keccack kec = new Nethereum.Util.Sha3Keccack();
            string func_hash = kec.CalculateHash(function);
            return func_hash.Substring(0, 8);
        }

        public static string ConvertUInt256ToHex(BigInteger val)
        {
            //This will return the hex format for a number with padded zeros
            //Bytes/bytes10/bytes32 are padded to the right, uints are padded to the left
            Nethereum.Hex.HexTypes.HexBigInteger hexint = new Nethereum.Hex.HexTypes.HexBigInteger(val);
            string hexint_string = ConvertByteArrayToHexString(hexint.ToHexByteArray());
            return hexint_string;
        }

        public static string GenerateEthereumDataParams(JArray computed_params)
        {
            //This function will generate the Ethereum ABI data field for fields used in NebliDex
            //Accounts for bytes32,bytes,address,uint256
            //Jobjects in Jarray must include data_type and data_hex, data_length required for bytes only
            string hex_data = "";
            int total_params = computed_params.Count; //All the dynamic data comes after the param fields for bytes
            int offset = total_params * 32; // The byte offset to start to write the dynamic data
            for (int i = 0; i < computed_params.Count; i++)
            {
                JObject param = (JObject)computed_params[i];
                string data_type = param["data_type"].ToString();
                if (data_type == "bytes")
                {
                    //This requires a data offset position
                    param["data_offset"] = offset;
                    int data_length = Convert.ToInt32(param["data_length"].ToString()); //The amount of bytes in the data
                    int factor = data_length % 32;
                    factor++; //We must pad data with 0 bytes to make it multiples of 32
                              //Recalculate the next offset position for data
                    offset = offset + 32 + factor * 32; //First 32 bytes for data length description, then the actual data
                    string data_hex = param["data_hex"].ToString();
                    data_hex = data_hex.PadRight(factor * 64, '0');
                    param["data_hex"] = data_hex;
                }
            }
            //Now we can write the data with padding in right direction, write only the non dynamic data now
            for (int i = 0; i < computed_params.Count; i++)
            {
                JObject param = (JObject)computed_params[i];
                string data_type = param["data_type"].ToString();
                string add_data = "";
                if (data_type == "bytes")
                {
                    //Write only the offsets for now
                    string offset_hex = ConvertUInt256ToHex(Convert.ToInt32(param["data_offset"].ToString()));
                    add_data = offset_hex.ToLower().PadLeft(64, '0');
                }
                else if (data_type == "bytes32")
                {
                    //Byte should already be hex, just pad right
                    string byte_hex = param["data_hex"].ToString();
                    add_data = byte_hex.ToLower().PadRight(64, '0');
                }
                else if (data_type == "address")
                {
                    string address_hex = param["data_hex"].ToString();
                    add_data = address_hex.PadLeft(64, '0');
                }
                else if (data_type == "uint256")
                {
                    string uint_hex = param["data_hex"].ToString();
                    add_data = uint_hex.PadLeft(64, '0');
                }
                hex_data += add_data;
            }
            //Now write the dynamic data
            for (int i = 0; i < computed_params.Count; i++)
            {
                JObject param = (JObject)computed_params[i];
                string data_type = param["data_type"].ToString();
                if (data_type == "bytes")
                {
                    //Write the amount of bytes the data is then write the data
                    string length_hex = ConvertUInt256ToHex(Convert.ToInt32(param["data_length"].ToString()));
                    hex_data += length_hex.ToLower().PadLeft(64, '0');
                    string byte_hex = param["data_hex"].ToString(); //This should already be padded to a multiple of 64
                    hex_data += byte_hex.ToLower();
                }
            }
            return hex_data;
        }
    }
	
}