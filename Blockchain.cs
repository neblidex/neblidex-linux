/*
 * Created by SharpDevelop.
 * User: David
 * Date: 2/22/2018
 * Time: 3:34 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */

//This handles all of the blockchain events, including transactions and validation

using System;
using System.IO;
using Gtk;
using System.Data;
using System.Numerics;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Policy;
using Mono.Data.Sqlite;
using System.Globalization;
using System.Threading.Tasks;

namespace NebliDex_Linux
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        
        public static System.Object transactionLock = new System.Object(); //This is a lock that prevents multiple active transactions
        public static decimal[] blockchain_fee = new decimal[7]; //Ordered based on cointype
        public static decimal ndex_fee = 10; //10 Total for trade, usually split per trader
        public static decimal[] dust_minimum = new decimal[7]; //The smallest UTXOUT for a transaction possible, otherwise it will be rejected
        public static bool testnet_mode = false; //Easy switch between testnet and main
        public static uint ntp1downcounter = 0; //If more than 2 down counts, network is down
        public static int lastvalidate_time = 0;

        //RPC Information
        public static bool using_blockhelper = false; //If true, the client will query its blockhelper instead of the API server (for now used only by CNs)
        public static bool using_cnblockhelper = false; //If true, the client with query a CN's blockhelper instead of API server
        public static string expected_blockhelper_version_prefix = "1"; //Blockhelper version should match this string
        public static int blockhelper_port = 6327; //The port at which the blockhelper listens.

        //Specific to creating transfer instructions
        public class NTP1Instructions
        {
            //Transfer instructions do not have a tokenid, they transfer based on the token position in the vins
            public ulong amount = 0; //Amount of token
            public int vout_num = 0; //Which vout
            public byte firstbyte = 0;
            public bool skipinput = false;
        }

        public static string GenerateMasterKey()
        {
            //Integer overflow is expected and part of generating the key
            Network my_net;
            if (testnet_mode == false)
            {
                my_net = Network.Main;
            }
            else
            {
                my_net = Network.TestNet;
            }
            ExtKey masterKey = new ExtKey();
            return masterKey.ToString(my_net);
        }

        public static ExtKey GeneratePrivateKey(string masterkey_string, int num)
        {
            //Private key path m/ or m/' (its harden)
            Network my_net;
            if (testnet_mode == false)
            {
                my_net = Network.Main;
            }
            else
            {
                my_net = Network.TestNet;
            }
            ExtKey masterKey = ExtKey.Parse(masterkey_string, my_net); //Reload the MasterKey
            ExtKey private_key = masterKey.Derive(num, true); //Get the order of this key and make it hardened (derived from private instead of public)
            return private_key;
        }

        public static string GenerateCoinAddress(ExtKey key, int wallet)
        {
            //This function will create an address specific to the wallet
			if (GetWalletBlockchainType(wallet) == 6) { return ""; } //Ethereum doesn't use this scheme base58
            Network my_net;
            if (testnet_mode == false)
            {
                my_net = Network.Main;
            }
            else
            {
                my_net = Network.TestNet;
            }
            //Modify the version byte
            ChangeVersionByte(wallet, ref my_net);
            return key.PrivateKey.PubKey.GetAddress(my_net).ToString();
        }

		public static void ChangeVersionByte(int wallet, ref Network my_net)
        {
            int blockchain = GetWalletBlockchainType(wallet);
            my_net.useGroestlHash = false; //Default

            if (testnet_mode == false)
            {
                if (blockchain == 0 || blockchain == 6)
                {
                    //Neblio and Ethereum (which doesn't use base58)
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (53) }; //Neblio (N)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (112) }; //Little n
                }
                else if (blockchain == 1 || blockchain == 4)
                {
                    //Bitcoin and Bitcoin Cash
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (0) }; //Bitcoin (1)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (5) }; //P2SH Address (3)
                }
                else if (blockchain == 2)
                {
                    //Litecoin
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (48) }; //Litecoin (L)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (50) }; //P2SH (M)
                }
                else if (blockchain == 3)
                {
                    //Groestlcoin
                    my_net.useGroestlHash = true;
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (36) }; //(F)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (5) }; //P2SH (3)
                }
                else if (blockchain == 5)
                {
                    //Monacoin
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (50) }; //(M)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (55) }; //P2SH (P)
                }
            }
            else
            {
                //Testnets
                if (blockchain == 0 || blockchain == 6)
                {
                    //Neblio and Ethereum (which doesn't use base58)
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (65) }; //Neblio (T)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (127) }; //Little t
                }
                else if (blockchain == 1 || blockchain == 4)
                {
                    //Bitcoin and Bitcoin Cash
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (111) }; //Bitcoin (m or n)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (196) }; //P2SH Address (2)
                }
                else if (blockchain == 2)
                {
                    //Litecoin
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (111) }; //Litecoin (m or n)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (196) }; //P2SH (2)
                }
                else if (blockchain == 3)
                {
                    //Groestlcoin
                    my_net.useGroestlHash = true;
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (111) }; // (m or n)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (196) }; //P2SH (2)
                }
                else if (blockchain == 5)
                {
                    //Monacoin
                    my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (111) }; //(m or n)
                    my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (117) }; //P2SH (p)
                }
            }
        }

        public static bool IsScriptHash(string address, Network my_net)
        {
			//Bitcoin Cash addresses must already be converted
            byte[] mybytes = Encoders.Base58Check.DecodeData(address);
            byte[] version_p2pkh = my_net.base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS];
            byte[] version_p2sh = my_net.base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS];
            if (mybytes[0] == version_p2sh[0])
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static Script GetAddressScriptPubKey(string address, int wallet)
        {
            //Get the scriptpubkey of an address regardless of if its a scripthash or pubkeyhash
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
                ChangeVersionByte(wallet, ref my_net);
                //And get the public key for the addresses
                if (IsScriptHash(address, my_net) == false)
                {
                    BitcoinPubKeyAddress for_address = new BitcoinPubKeyAddress(address, my_net);
                    return for_address.ScriptPubKey;
                }
                else
                {
                    BitcoinScriptAddress for_address_script = new BitcoinScriptAddress(address, my_net); //Like a multisig address
                    return for_address_script.ScriptPubKey;
                }
            }
        }

        public static string GetElectrumScriptHash(string address, int wallet)
        {
            //This method will take the address, get the scriptpubkey, hash using sha256 then reverse the bytes
            Script scr = GetAddressScriptPubKey(address, wallet);
            byte[] hex_bytes = NBitcoin.Crypto.Hashes.SHA256(scr.ToBytes());
            Array.Reverse(hex_bytes);
            string hex = ConvertByteArrayToHexString(hex_bytes);
            return hex;
        }

        public static string Address2PublicKeyHash(string address)
        {
            //Remove the version byte
            byte[] mybytes = Encoders.Base58Check.DecodeData(address);
            string pubhash = BitConverter.ToString(mybytes).Replace("-", "");
            return pubhash.Remove(0, 2); //Remove the version information
        }

		public static Transaction CreateSignedP2PKHTx(int wallet, decimal amount, string address, bool broadcast, bool exactamount, string val_add = "", decimal val_fee = 0, decimal extra_neb = 0, bool redeem_extra = false)
        {

            //This will take the balance of the transaction, calculate the change from the amount, then send back to you

            decimal totalamount = 0;
            decimal change = 0;

            bool wallet_ntp1 = IsWalletNTP1(wallet);
            if (wallet_ntp1 == false)
            {
                totalamount = GetWalletAmount(wallet);
                change = totalamount - amount; //Simple calculation
            }
            else
            {
                totalamount = GetWalletAmount(0); //All fees and change are based on NEBL balance
                change = totalamount; //We are not modifying the NEBL by sending tokens, only miner fee and op_return fee
                                      //Predicting change is not so easy here
            }
            if (change < 0) { return null; } //The amount is greater than the wallet balance

            int connectiontype = GetWalletBlockchainType(wallet);

            Decimal dust_decimal = dust_minimum[connectiontype];
            dust_decimal = Decimal.Multiply(dust_decimal, 100000000);
            BigInteger dust_output = new BigInteger(dust_decimal); //Get the dust minimum

            //First find the correct dex connection for electrum
            try
            {
                DexConnection dex = null;

                if (connectiontype != 0)
                {
                    lock (DexConnectionList)
                    {
                        for (int i = 0; i < DexConnectionList.Count; i++)
                        {
                            if (DexConnectionList[i].contype == 1 && DexConnectionList[i].open == true && DexConnectionList[i].blockchain_type == connectiontype)
                            {
                                dex = DexConnectionList[i];
                                break;
                            }
                        }
                    }
                } //Neblio doesn't use the dex connections

                string my_address = "";
                string my_extended_private_key_string = "";
                for (int i = 0; i < WalletList.Count; i++)
                {
                    if (WalletList[i].type == wallet)
                    {
                        my_address = WalletList[i].address;
                        my_extended_private_key_string = WalletList[i].private_key;
                        break;
                    }
                }

                if (dex == null && connectiontype != 0)
                {
                    NebliDexNetLog("Could not find a connection to a Dex");
                    return null;
                } //Couldn't find our connection
                if (my_address == "") { return null; } //No address

                //Now we can fetch the unspent transactions
                //Will only fetch amounts from transactions
                JArray utxo = GetAddressUnspentTX(dex, wallet, my_address);
                //If Neblio, will return unspent tx including token information

                if (utxo == null)
                {
                    NebliDexNetLog("No unspent transaction amounts found");
                    return null;
                }

                //Create the transaction along with the prediction transaction
                Transaction tx = new Transaction();
                Transaction predict_tx = new Transaction(); //Used to predict the miner fee

                if (connectiontype == 0)
                {
                    //This is a Neblio based wallet, transaction has timestamp in it
                    tx.hasTimeStamp = true;
                    predict_tx.hasTimeStamp = true;
                }
                else if (connectiontype == 4)
                {
                    //Bitcoin Cash has a different hashing than Bitcoin
                    tx.useForkID = true;
                    predict_tx.useForkID = true;
                    tx.Version = 2;
                }
                else if (connectiontype == 3)
                {
                    //Groestlcoin uses a different set of hashing than Bitcoin as wel
                    tx.useHASH256 = false;
                    predict_tx.useHASH256 = false;
                }

                bool sendtoken = false;

                if (val_fee > 0 || wallet_ntp1 == true)
                {
                    sendtoken = true;
                }

                //Make a list of the input txins
                List<BigInteger> utxo_values = new List<BigInteger>();

                //This is for a normal transaction
                //Go through each row of results
                if (wallet_ntp1 == false && sendtoken == false)
                {
                    BigInteger total_tx_amount = 0;

                    Decimal totalamount_dec = totalamount;

                    //Then multiply it, for satoshi amounts
                    totalamount_dec = Decimal.Multiply(totalamount_dec, 100000000);

                    //Convert the satoshi decimal to biginteger
                    BigInteger satoshi_total = new BigInteger(totalamount_dec);

                    int total_utxo = 0;

                    foreach (JToken row in utxo)
                    {
                        //Get the tx_hash
                        string tx_hash = row["tx_hash"].ToString();
                        int tx_pos = Convert.ToInt32(row["tx_pos"].ToString()); //The index of the utxout from this tx
                        BigInteger tx_amount = BigInteger.Parse(row["tx_value"].ToString()); //The amount

                        OutPoint utxout = OutPoint.Parse(tx_hash + "-" + tx_pos); //Create the Outpoint
                        TxIn tx_in = new TxIn();
                        tx_in.PrevOut = utxout;

                        if (wallet == 0)
                        {
                            string tokid = row["tx_tokenid"].ToString();
                            if (tokid.Length == 0)
                            {
                                //We can use this normal unspent txout
                                //Do not include unspends that have tokens in them
                                total_tx_amount = BigInteger.Add(total_tx_amount, tx_amount);
                                tx.Inputs.Add(tx_in); //Add to the inputs
                                predict_tx.Inputs.Add(tx_in);
                                utxo_values.Add(tx_amount);
                            }
                        }
                        else
                        {
                            total_tx_amount = BigInteger.Add(total_tx_amount, tx_amount);
                            tx.Inputs.Add(tx_in); //Add to the inputs
                            predict_tx.Inputs.Add(tx_in);
                            utxo_values.Add(tx_amount);
                        }
                        total_utxo++;
                    }

                    if (total_tx_amount == 0) { NebliDexNetLog("No unspent transactions found."); return null; } //Nothing to spend
                    if (total_tx_amount.Equals(satoshi_total) == false)
                    {
                        NebliDexNetLog("Unspent total doesn't match account total.");
                        if (total_utxo < 900)
                        {
                            return null;
                        }
                        else
                        {
                            //There are too many unspents on this account, we need to consolidate them
                            NebliDexNetLog("But we need to consolidate the unspents, there are too many so continue");
                        }
                    } //The sizes are not equal

                }
                else if (sendtoken == true)
                {
                    //Use the NEBL send functions
                    //Get the potential token transaction from my wallet
                    Tuple<Transaction, Decimal> result_tup = GenerateTokenTransactionHex(wallet, amount, GetWalletAddress(wallet), address, val_add, val_fee, false);
                    if (result_tup == null)
                    {
                        NebliDexNetLog("Failed to create NTP1 transaction");
                        return null;
                    }

                    Transaction template_tx = result_tup.Item1; //Get the transaction from the tuple
                    tx = template_tx;
                    predict_tx = new Transaction(tx.ToHex(), true);
                    Decimal nebl_calc_unspent_total = result_tup.Item2; //Will represent the total satoshi value of the inputs from the template

                    //New way, we just add the neblio only inputs to the transaction, all the token inputs are already there
                    foreach (JToken row in utxo)
                    {
                        //Get the tx_hash
                        string tx_hash = row["tx_hash"].ToString();
                        uint tx_pos = Convert.ToUInt32(row["tx_pos"].ToString()); //The index of the utxout from this tx
                        string tokid = row["tx_tokenid"].ToString();
                        if (tokid.Length == 0)
                        { //Pure Neblio, add to the inputs
                            OutPoint utxout = OutPoint.Parse(tx_hash + "-" + tx_pos); //Create the Outpoint
                            TxIn tx_in = new TxIn();
                            tx_in.PrevOut = utxout;

                            Decimal row_amount = Decimal.Parse(row["tx_value"].ToString()); //Add the row
                            nebl_calc_unspent_total = Decimal.Add(nebl_calc_unspent_total, row_amount);

                            tx.Inputs.Add(tx_in); //Add to the inputs
                            predict_tx.Inputs.Add(tx_in);
                        }
                    }

                    if (nebl_calc_unspent_total == 0) { NebliDexNetLog("No unspent NEBL found."); return null; } //Nothing to spend

                    //Also get the amount of already created outputs
                    Decimal nebl_spending_total = 0;
                    for (int i = 0; i < tx.Outputs.Count; i++)
                    {
                        nebl_spending_total = Decimal.Add(nebl_spending_total, tx.Outputs[i].Value.ToDecimal(MoneyUnit.Satoshi));
                    }

                    //We will calculate how much change we need to send back to the sending account
                    Decimal change_sat = Decimal.Subtract(nebl_calc_unspent_total, nebl_spending_total);
                    change_sat = Decimal.Divide(change_sat, 100000000);
                    change = change_sat; //This is our real max change amount
                    if (change_sat < 0)
                    {
                        NebliDexNetLog("Not enough input to cover all the fees in this transaction");
                        return null;
                    }

                    if (wallet_ntp1 == true)
                    {
                        amount = 0; //We do not want to send NEBL to the to address
                    }
                    change -= amount;
                }

                //From here on, the amount only refers to coin amounts, not token

                //This function can send money to a script hash as well
                BitcoinPubKeyAddress to_address = null;
                BitcoinScriptAddress to_address_script = null;
                BitcoinPubKeyAddress change_address = null;
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
                    ChangeVersionByte(wallet, ref my_net);
                    //And get the public key for the addresses
                    if (IsScriptHash(address, my_net) == false)
                    {
                        to_address = new BitcoinPubKeyAddress(address, my_net);
                    }
                    else
                    {
                        to_address_script = new BitcoinScriptAddress(address, my_net); //Like a multisig address
                    }
                    change_address = new BitcoinPubKeyAddress(my_address, my_net);
                    priv_key = ExtKey.Parse(my_extended_private_key_string, my_net);
                }

                //Create a fake output and sign it to calculate the real fee
                TxOut tout = new TxOut()
                {
                    Value = new Money(new BigInteger(100000000)),
                    ScriptPubKey = change_address.ScriptPubKey
                };
                predict_tx.Outputs.Add(tout);

                if (extra_neb > 0 || wallet_ntp1 == false)
                {
                    //NTP1 sends do not have another TO address unless we want it to
                    //Only Neblio sends
                    tout = new TxOut()
                    {
                        Value = new Money(new BigInteger(amount * 100000000)),
                        ScriptPubKey = change_address.ScriptPubKey
                    };
                    predict_tx.Outputs.Add(tout);
                }

                //Put the pubkeys in the fake inputs
                for (int i = 0; i < predict_tx.Inputs.Count; i++)
                {
                    predict_tx.Inputs[i].ScriptSig = change_address.ScriptPubKey; //Add our pub key to the script sig
                }

                //Now sign the fake transaction
                predict_tx.Sign(priv_key.PrivateKey, false);

                //Now calculate the transaction fee based on this length
                Decimal fee_per_byte = blockchain_fee[connectiontype];
                fee_per_byte = Decimal.Divide(fee_per_byte, 1000); //Per byte fee
                uint tx_bytes = Convert.ToUInt32(predict_tx.ToHex().Length / 2.0);

                //Determine the fees based on the inputs
                //Calculate Miner fee
                decimal miner_fee = Decimal.Multiply(tx_bytes, fee_per_byte); //Will change this number
                if (connectiontype == 0)
                {
                    //Neblio fee has to be in multiples of 10,000 satoshi (0.0001)
                    //Per Eddy, this value is rounded up
                    miner_fee = Math.Ceiling(miner_fee / 0.0001m) * 0.0001m;
                }
                else
                {
                    if (miner_fee < 0.00001m)
                    {
                        //Bitcoin and Litecoin Minimum relay fee
                        miner_fee = 0.00001m;
                    }
                }

                change -= miner_fee; //Take the miner fee from the change

                //If we are being generous and sending extra NEBL to do a token transaction
                if (extra_neb > 0)
                {
                    amount += extra_neb;
                    change -= extra_neb;
                }

                if (to_address_script != null && redeem_extra == true)
                {
                    //We are sending to a multisig wallet, so send some extra to cover the eventual transfer
                    //We must also agree to pay extra for the multsig
                    //We will use a fee that estimates based on the average multsig out transaction of 400 bytes
                    decimal multisig_fee = Decimal.Multiply(400, fee_per_byte);
                    if (amount < blockchain_fee[connectiontype] * 2)
                    {
                        //We are sending a small amount
                        multisig_fee = multisig_fee * 2; //Double the fee
                    }

                    if (connectiontype == 0)
                    {
                        //Neblio fee has to be in multiples of 10,000 satoshi (0.0001)
                        //Per Eddy, this value is rounded up
                        multisig_fee = Math.Ceiling(multisig_fee / 0.0001m) * 0.0001m;
                        if (sendtoken == true)
                        {
                            //We are sending token to redeemscript
                            if (multisig_fee < 0.0002m) { multisig_fee = 0.0002m; }
                            //We need extra balance to at least cover OP_RETURN and miner fee for eventual transfer
                        }
                    }
                    if (miner_fee > multisig_fee)
                    {
                        multisig_fee = miner_fee;
                    }
                    amount += multisig_fee;
                    change -= multisig_fee;
                }

                if (change < 0)
                {
                    if (exactamount == true)
                    {
                        NebliDexNetLog("Not enough coin to cover exact amount");
                        return null;
                    } //We must send an exact amount to sender otherwise fail

                    //change was not big enough to handle miner fee, so take from amount sent
                    amount = amount + change; //Subtract from amount, the negative amount of change
                                              //If extra neb, subtract from that too, if we can't support it
                    change = 0;
                    if (amount < 0)
                    {
                        NebliDexNetLog("Not enough funds to pay for transaction.");
                        return null;
                    } //Amount was too small
                }

                Decimal amount_dec = amount;
                Decimal change_dec = change;

                //Convert to satoshi
                amount_dec = Decimal.Multiply(amount_dec, 100000000);
                change_dec = Decimal.Multiply(change_dec, 100000000);

                //Convert this to BigInteger
                BigInteger satoshi_amount = new BigInteger(amount_dec);
                BigInteger satoshi_change = new BigInteger(change_dec);

                //Now we determine where to send the coins
                //We will place the change txout before the to txout because token function doesn't recognize otherwise

                //For token transactions, the to address is already included
                if (satoshi_amount >= dust_output)
                { //If less than dust, do not send
                    if (to_address != null)
                    {
                        tout = new TxOut()
                        {
                            Value = new Money(satoshi_amount),
                            ScriptPubKey = to_address.ScriptPubKey
                        };
                    }
                    else
                    {
                        tout = new TxOut()
                        {
                            Value = new Money(satoshi_amount),
                            ScriptPubKey = to_address_script.ScriptPubKey
                        };
                    }
                    tx.Outputs.Add(tout);
                }

                if (satoshi_change > dust_output)
                { //Send change back
                    tout = new TxOut()
                    {
                        Value = new Money(satoshi_change),
                        ScriptPubKey = change_address.ScriptPubKey
                    };

                    tx.Outputs.Add(tout);
                }

                for (int i = 0; i < tx.Inputs.Count; i++)
                {
                    tx.Inputs[i].ScriptSig = change_address.ScriptPubKey; //Add our pub key to the script sig (when signing, they will be replaced)
                }
                //ScriptSig for multisig before signing will be the redeemscript
                //It must be signed by both people for it to be transmitted

                //Now sign the real transaction
                if (utxo_values.Count == 0)
                {
                    tx.Sign(priv_key.PrivateKey, false);
                }
                else
                {
                    //Or use the UTXO values for the scriptsigs
                    tx.Sign(priv_key.PrivateKey, false, utxo_values);
                }

                if (broadcast == true)
                {
                    //Send the transaction
                    bool timeout;
                    string id = TransactionBroadcast(wallet, tx.ToHex(), out timeout);
                    if (timeout == false)
                    {
                        if (id.Length == 0)
                        {
                            //Failed to broadcast
                            NebliDexNetLog("Failed to broadcast completely: " + tx.GetHash().ToString());
                            //Even if the broadcasting fails, we want to program to act as though it didn't
                            //In cases that it doesn't actually fail, we still want to monitor the potential transaction
                        }
                        //Make the wallet associated with this transaction unavailable
                        UpdateWalletStatus(wallet, 2);
                    }
                    else
                    {
                        //Not connected to internet
                        return null;
                    }
                }

                return tx; //This will return the signed transaction

            }
            catch (Exception e)
            {
                NebliDexNetLog("Error while creating transaction: " + e.ToString());
            }
            return null;
        }

		public static Transaction CreateNTP1AllTokenTransfer(string to_add)
        {
            //This function will create a transaction that sends all tokens and to a particular address in one transaction
            //It can only send a maximum of 32 different token types.
            //TODO: Develop a plan to send more than 32 different token types at once (may have to build multiple simultaneous transactions)
            int wallet = 0;
            string from_add = GetWalletAddress(wallet);
            decimal gas_fee = GetWalletAmount(wallet);
            if (gas_fee < blockchain_fee[0] * 2) { return null; } //Not enough NEBL to pay for fees
            int connectiontype = 0;

            Decimal dust_decimal = dust_minimum[connectiontype];
            dust_decimal = Decimal.Multiply(dust_decimal, 100000000);
            BigInteger dust_output = new BigInteger(dust_decimal); //Get the dust minimum

            //First find the correct dex connection for electrum
            try
            {
                //Now we can fetch the unspent transactions
                JArray utxo = GetAddressUnspentTX(null, wallet, from_add);
                if (utxo == null) { return null; }

                //Create the transaction along with the prediction transaction
                Transaction tx = new Transaction();
                Transaction predict_tx = new Transaction(); //Used to predict the miner fee
                tx.hasTimeStamp = true;
                predict_tx.hasTimeStamp = true;

                //Get the wallet private key for neblio
                string my_extended_private_key_string = "";
                for (int i = 0; i < WalletList.Count; i++)
                {
                    if (WalletList[i].type == wallet)
                    {
                        my_extended_private_key_string = WalletList[i].private_key;
                        break;
                    }
                }

                List<string> sendtoken = new List<string>(); //This is a list of all the tokens to send, in order
                List<int> sendtoken_wallet = new List<int>(); //The wallet matching the ID
                for (int i = 0; i < WalletList.Count; i++)
                {
                    if (IsWalletNTP1(WalletList[i].type) == true)
                    {
                        int tokwal = WalletList[i].type;
                        string tokid = GetWalletTokenID(tokwal);
                        if (tokid.Length == 0) { continue; }
                        Decimal bal = GetBlockchainAddressBalance(tokwal, from_add, false);
                        Decimal bal2 = GetWalletAmount(tokwal);
                        if (bal2.Equals(bal) == false) { return null; } //These should be the same
                        if (bal > 0)
                        {
                            //This will tell us if we need to send this token too
                            sendtoken.Add(tokid);
                            sendtoken_wallet.Add(tokwal);
                        }
                    }
                }

                //Now create the token transaction if tokens exist
                Decimal nebl_calc_unspent_total = 0;
                if (sendtoken.Count > 0)
                {
                    JObject request = new JObject(); //We will recreate our sendtoken request
                    request["fee"] = 0; //We will handle the fees

                    //Make sure change is split from normal change always
                    JObject splitchange = new JObject();
                    splitchange["splitChange"] = true;
                    request["flags"] = splitchange;

                    request["from"] = new JArray(from_add);
                    JArray to_array = new JArray();
                    for (int i = 0; i < sendtoken.Count; i++)
                    {
                        JObject to = new JObject();
                        to["address"] = to_add;
                        to["amount"] = Convert.ToInt32(GetWalletAmount(sendtoken_wallet[i]));
                        to["tokenId"] = GetWalletTokenID(sendtoken_wallet[i]);
                        to_array.Add(to);
                    }
                    //This arary should have all the tokens
                    request["to"] = to_array;
                    string json = JsonConvert.SerializeObject(request);
                    NebliDexNetLog("Sending all tokens to newly created address " + to_add + " via sendtoken request: " + json);
                    tx = GenerateScratchNTP1Transaction(from_add, to_array, ref nebl_calc_unspent_total);
                    if (tx == null) { return null; }
                    predict_tx = new Transaction(tx.ToHex(), true);

                }

                //Now add the Neblio input
                //New way, we just add the neblio only inputs to the transaction, all the token inputs are already there
                foreach (JToken row in utxo)
                {
                    //Get the tx_hash
                    string tx_hash = row["tx_hash"].ToString();
                    uint tx_pos = Convert.ToUInt32(row["tx_pos"].ToString()); //The index of the utxout from this tx
                    string tokid = row["tx_tokenid"].ToString();
                    if (tokid.Length == 0)
                    { //Pure Neblio, add to the inputs
                        OutPoint utxout = OutPoint.Parse(tx_hash + "-" + tx_pos); //Create the Outpoint
                        TxIn tx_in = new TxIn();
                        tx_in.PrevOut = utxout;

                        Decimal row_amount = Decimal.Parse(row["tx_value"].ToString()); //Add the row
                        nebl_calc_unspent_total = Decimal.Add(nebl_calc_unspent_total, row_amount);

                        tx.Inputs.Add(tx_in); //Add to the inputs
                        predict_tx.Inputs.Add(tx_in);
                    }
                }

                if (nebl_calc_unspent_total == 0) { NebliDexNetLog("No unspent NEBL found."); return null; }
                //Also get the amount of already created outputs
                Decimal nebl_spending_total = 0;
                for (int i = 0; i < tx.Outputs.Count; i++)
                {
                    nebl_spending_total = Decimal.Add(nebl_spending_total, tx.Outputs[i].Value.ToDecimal(MoneyUnit.Satoshi));
                }
                //We will calculate how much change we need to send back to the sending account
                Decimal send_amount_sat = Decimal.Subtract(nebl_calc_unspent_total, nebl_spending_total);
                if (send_amount_sat < 0)
                {
                    NebliDexNetLog("Not enough input to cover the fees for this transaction");
                    return null;
                }
                decimal send_amount = Decimal.Divide(send_amount_sat, 100000000); //This will be the NEBL used to pay the fees

                BitcoinPubKeyAddress to_address = null;
                BitcoinPubKeyAddress from_address = null;
                ExtKey priv_key = null;
                lock (transactionLock)
                {
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
                    ChangeVersionByte(wallet, ref my_net);

                    if (IsScriptHash(to_add, my_net) == true)
                    {
                        return null; //Can only send to PKH addresses
                    }
                    //And get the public key for the address
                    to_address = new BitcoinPubKeyAddress(to_add, my_net);
                    from_address = new BitcoinPubKeyAddress(from_add, my_net);
                    priv_key = ExtKey.Parse(my_extended_private_key_string, my_net);
                }

                //Create a fake output and sign it to calculate the real fee
                TxOut tout = new TxOut()
                {
                    Value = new Money(100000000),
                    ScriptPubKey = to_address.ScriptPubKey
                };
                predict_tx.Outputs.Add(tout);

                //Put the pubkeys in the fake inputs
                for (int i = 0; i < predict_tx.Inputs.Count; i++)
                {
                    predict_tx.Inputs[i].ScriptSig = from_address.ScriptPubKey;
                }

                //Now sign the fake transaction
                predict_tx.Sign(priv_key.PrivateKey, false);

                //Now calculate the transaction fee based on this length
                Decimal fee_per_byte = blockchain_fee[connectiontype];
                fee_per_byte = Decimal.Divide(fee_per_byte, 1000); //Per byte fee
                uint tx_bytes = Convert.ToUInt32(predict_tx.ToHex().Length / 2.0);

                //Determine the fees based on the inputs
                //Calculate Miner fee
                decimal miner_fee = Decimal.Multiply(tx_bytes, fee_per_byte); //Will change this number
                if (connectiontype == 0)
                {
                    //Neblio fee has to be in multiples of 10,000 satoshi (0.0001)
                    //Per Eddy, this value is rounded up
                    miner_fee = Math.Ceiling(miner_fee / 0.0001m) * 0.0001m;
                }

                send_amount -= miner_fee; //Take the miner fee from the amount sent

                if (send_amount < 0)
                { //But 0 is ok
                    return null;
                }

                Decimal send_amount_dec = send_amount;

                //Convert to satoshi
                send_amount_dec = Decimal.Multiply(send_amount_dec, 100000000);

                //Convert this to BigInteger
                BigInteger satoshi_send_amount = new BigInteger(send_amount_dec);

                //Now we determine where to send the coins
                if (satoshi_send_amount >= dust_output)
                { //May not have a to_address if less than dust
                    tout = new TxOut()
                    {
                        Value = new Money(satoshi_send_amount),
                        ScriptPubKey = to_address.ScriptPubKey
                    };
                    tx.Outputs.Add(tout);
                }

                for (int i = 0; i < tx.Inputs.Count; i++)
                {
                    tx.Inputs[i].ScriptSig = from_address.ScriptPubKey;
                }

                //Now sign the real transaction
                tx.Sign(priv_key.PrivateKey, false);

                return tx;

            }
            catch (Exception e)
            {
                NebliDexNetLog("Error while creating NTP1 all tokens transaction: " + e.ToString());
            }
            return null;
        }

		public static Transaction CreateAtomicSwapP2SHTx(int wallet, string swap_add, string to_add, string redeemscript_string, uint unlock_time, string secret, bool refund)
        {

            //This function is specifically for redeeming or refunding a balance from a atomic swap address
            //It will transfer the entire balance of the redeem script
            //Because the fee can vary, it will have to remove the fee from the send_amount
            //Secret is stored as a hex string of a byte array
            //Secret hash is hex string of byte array

            Decimal swap_balance_sat = GetBlockchainAddressBalance(wallet, swap_add, true);
            //Multisig balance will be satoshi values if coming from non-token wallet

            decimal send_amount = 0; //This is the amount pure coin we are sending to the redeemer

            if (swap_balance_sat <= 0)
            {
                return null; //There are no funds in this account
            }

            int connectiontype = GetWalletBlockchainType(wallet);

            Decimal dust_decimal = dust_minimum[connectiontype];
            dust_decimal = Decimal.Multiply(dust_decimal, 100000000);
            BigInteger dust_output = new BigInteger(dust_decimal); //Get the dust minimum

            //First find the correct dex connection for electrum
            try
            {
                DexConnection dex = null;

                if (connectiontype != 0)
                {
                    lock (DexConnectionList)
                    {
                        for (int i = 0; i < DexConnectionList.Count; i++)
                        {
                            if (DexConnectionList[i].contype == 1 && DexConnectionList[i].open == true && DexConnectionList[i].blockchain_type == connectiontype)
                            {
                                dex = DexConnectionList[i];
                                break;
                            }
                        }
                    }
                } //Neblio doesn't use the dex connections

                if (dex == null && connectiontype != 0) { return null; } //Couldn't find our connection

                //Now we can fetch the unspent transactions
                JArray utxo = GetAddressUnspentTX(dex, wallet, swap_add);
                if (utxo == null) { return null; }

                //Create the transaction along with the prediction transaction
                Transaction tx = new Transaction();
                Transaction predict_tx = new Transaction(); //Used to predict the miner fee

                if (connectiontype == 0)
                {
                    //This is a Neblio based wallet, transaction has timestamp in it
                    tx.hasTimeStamp = true;
                    predict_tx.hasTimeStamp = true;
                }
                else if (connectiontype == 4)
                {
                    //This is a Bitcoin Cash hash
                    tx.useForkID = true;
                    predict_tx.useForkID = true;
                    tx.Version = 2;
                }
                else if (connectiontype == 3)
                {
                    //Groestlcoin uses a different set of hashing than Bitcoin as well
                    tx.useHASH256 = false;
                    predict_tx.useHASH256 = false;
                }

                bool sendtoken = false;
                bool wallet_ntp1 = IsWalletNTP1(wallet);
                if (wallet_ntp1 == true)
                {
                    //Either Neblio with fee in same transaction or token with fee, or just token
                    sendtoken = true;
                }

                //This is for a normal transaction
                //Go through each row of results

                //Make a list of the input txins
                List<BigInteger> utxo_values = new List<BigInteger>();

                if (wallet_ntp1 == false && sendtoken == false)
                {
                    BigInteger total_tx_amount = 0;
                    BigInteger satoshi_total = new BigInteger(swap_balance_sat);

                    int total_utxo = 0;

                    foreach (JToken row in utxo)
                    {
                        //Get the tx_hash
                        string tx_hash = row["tx_hash"].ToString();
                        int tx_pos = Convert.ToInt32(row["tx_pos"].ToString()); //The index of the utxout from this tx
                        BigInteger tx_amount = BigInteger.Parse(row["tx_value"].ToString()); //The amount

                        OutPoint utxout = OutPoint.Parse(tx_hash + "-" + tx_pos); //Create the Outpoint
                        TxIn tx_in = new TxIn();
                        tx_in.PrevOut = utxout;

                        if (wallet == 0)
                        {
                            string tokid = row["tx_tokenid"].ToString();
                            if (tokid.Length == 0)
                            {
                                //We can use this normal unspent txout
                                //Do not include unspends that have tokens in them
                                total_tx_amount = BigInteger.Add(total_tx_amount, tx_amount);
                                tx.Inputs.Add(tx_in); //Add to the inputs
                                predict_tx.Inputs.Add(tx_in);
                                utxo_values.Add(tx_amount);
                            }
                        }
                        else
                        {
                            total_tx_amount = BigInteger.Add(total_tx_amount, tx_amount);
                            tx.Inputs.Add(tx_in); //Add to the inputs
                            predict_tx.Inputs.Add(tx_in);
                            utxo_values.Add(tx_amount);
                        }
                        total_utxo++;
                    }

                    if (total_tx_amount == 0) { NebliDexNetLog("No unspent transactions found."); return null; } //Nothing to spend
                    if (total_tx_amount.Equals(satoshi_total) == false)
                    {
                        NebliDexNetLog("Unspent total doesn't match account total.");
                        if (total_utxo < 900)
                        {
                            return null;
                        }
                        else
                        {
                            //There are too many unspents on this account, we need to consolidate them
                            NebliDexNetLog("But we need to consolidate the unspents, there are too many so continue");
                        }
                    } //The sizes are not equal

                    send_amount = Decimal.Divide(swap_balance_sat, 100000000);
                }
                else if (sendtoken == true)
                {
                    //Use the NEBL send functions
                    //Get the potential token transaction

                    Tuple<Transaction, Decimal> result_tup = GenerateTokenTransactionHex(wallet, swap_balance_sat, swap_add, to_add, "", 0, true);
                    if (result_tup == null)
                    {
                        NebliDexNetLog("Failed to create NTP1 transaction");
                        return null;
                    }
                    Transaction template_tx = result_tup.Item1;
                    tx = template_tx;
                    predict_tx = new Transaction(template_tx.ToHex(), true);
                    Decimal nebl_calc_unspent_total = result_tup.Item2; //Will represent the total satoshi value of the inputs from the template

                    //New way, we just add the neblio only inputs to the transaction, all the token inputs are already there
                    foreach (JToken row in utxo)
                    {
                        //Get the tx_hash
                        string tx_hash = row["tx_hash"].ToString();
                        uint tx_pos = Convert.ToUInt32(row["tx_pos"].ToString()); //The index of the utxout from this tx
                        string tokid = row["tx_tokenid"].ToString();
                        if (tokid.Length == 0)
                        { //Pure Neblio, add to the inputs
                            OutPoint utxout = OutPoint.Parse(tx_hash + "-" + tx_pos); //Create the Outpoint
                            TxIn tx_in = new TxIn();
                            tx_in.PrevOut = utxout;

                            Decimal row_amount = Decimal.Parse(row["tx_value"].ToString()); //Add the row
                            nebl_calc_unspent_total = Decimal.Add(nebl_calc_unspent_total, row_amount);

                            tx.Inputs.Add(tx_in); //Add to the inputs
                            predict_tx.Inputs.Add(tx_in);
                        }
                    }

                    if (nebl_calc_unspent_total == 0) { NebliDexNetLog("No unspent NEBL found."); return null; } //Nothing to spend

                    //Also get the amount of already created outputs
                    Decimal nebl_spending_total = 0;
                    for (int i = 0; i < tx.Outputs.Count; i++)
                    {
                        nebl_spending_total = Decimal.Add(nebl_spending_total, tx.Outputs[i].Value.ToDecimal(MoneyUnit.Satoshi));
                    }

                    //We will calculate how much change we need to send to the sending account
                    Decimal send_amount_sat = Decimal.Subtract(nebl_calc_unspent_total, nebl_spending_total);
                    if (send_amount_sat < 0)
                    {
                        NebliDexNetLog("Not enough input to cover the fees for this transaction");
                        return null;
                    }

                    send_amount = Decimal.Divide(send_amount_sat, 100000000); //This will be the NEBL used to pay the fees
                }

                //Also get the extkey
                string my_extended_private_key_string = "";
                for (int i = 0; i < WalletList.Count; i++)
                {
                    if (WalletList[i].type == wallet)
                    {
                        my_extended_private_key_string = WalletList[i].private_key;
                        break;
                    }
                }

                BitcoinPubKeyAddress to_address = null;
                ExtKey priv_key = null;
                lock (transactionLock)
                {
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
                    ChangeVersionByte(wallet, ref my_net);

                    if (IsScriptHash(to_add, my_net) == true)
                    {
                        return null; //Can only send to PKH addresses
                    }
                    //And get the public key for the address
                    to_address = new BitcoinPubKeyAddress(to_add, my_net);
                    priv_key = ExtKey.Parse(my_extended_private_key_string, my_net);
                }

                //Create a fake output and sign it to calculate the real fee
                TxOut tout = new TxOut()
                {
                    Value = new Money(100000000),
                    ScriptPubKey = to_address.ScriptPubKey
                };
                predict_tx.Outputs.Add(tout);
                tout = new TxOut()
                {
                    Value = new Money(100000000),
                    ScriptPubKey = to_address.ScriptPubKey
                };
                predict_tx.Outputs.Add(tout);

                //Adjust the locktime of the transaction so that it is time sensitive               
                if (refund == true)
                {
                    //If there is a refund to be had, unlocktime must be in the past
                    tx.LockTime = unlock_time;
                    predict_tx.LockTime = unlock_time;
                }

                if (refund == true)
                {
                    //Set all the nSquences to zero as this is a hashlock transaction
                    for (int i = 0; i < predict_tx.Inputs.Count; i++)
                    {
                        predict_tx.Inputs[i].Sequence = 0; //This must be allowed to use checklocktimeverify
                    }
                }

                //Put the pubkeys in the fake inputs                
                for (int i = 0; i < predict_tx.Inputs.Count; i++)
                {
                    //Create the scriptsig for each input
                    Script redeem_script = new Script(redeemscript_string);
                    ScriptCoin redeem_coin = new ScriptCoin(predict_tx.Inputs[i].PrevOut, new TxOut(10000, redeem_script.PaymentScript), redeem_script);
                    TransactionSignature sig = predict_tx.SignInput(priv_key.PrivateKey, redeem_coin); //Sign the mock transaction
                    Script p2pkh_script = PayToPubkeyHashTemplate.Instance.GenerateScriptSig(sig, priv_key.PrivateKey.PubKey); //Get the scriptsig that represents a typical p2pkh
                    Script final_scriptsig = null;
                    if (refund == false)
                    {
                        //We are paying to my address, utilize the secret
                        Op secret_bytes = Op.GetPushOp(ConvertHexStringToByteArray(secret));
                        //Script sig: Signature Pubkey Secret OP_1 Redeem_Script
                        final_scriptsig = p2pkh_script + secret_bytes + OpcodeType.OP_1 + Op.GetPushOp(redeem_script.ToBytes()); //Redeem script is initially push data
                    }
                    else
                    {
                        //Failed payment, get refund after certain time
                        //Script sig: Signature Pubkey OP_0 Redeem_Script
                        final_scriptsig = p2pkh_script + OpcodeType.OP_0 + Op.GetPushOp(redeem_script.ToBytes()); //Redeem script is initially push data
                    }
                    predict_tx.Inputs[i].ScriptSig = final_scriptsig; //This is a signed scriptsig
                }

                //Now calculate the transaction fee based on this length
                Decimal fee_per_byte = blockchain_fee[connectiontype];
                fee_per_byte = Decimal.Divide(fee_per_byte, 1000); //Per byte fee
                uint tx_bytes = Convert.ToUInt32(predict_tx.ToHex().Length / 2.0);

                //Determine the fees based on the inputs
                //Calculate Miner fee
                decimal miner_fee = Decimal.Multiply(tx_bytes, fee_per_byte); //Will change this number
                if (connectiontype == 0)
                {
                    //Neblio fee has to be in multiples of 10,000 satoshi (0.0001)
                    //Per Eddy, this value is rounded up
                    miner_fee = Math.Ceiling(miner_fee / 0.0001m) * 0.0001m;
                }
                else
                {
                    if (miner_fee < 0.00001m)
                    {
                        //Bitcoin Minimum relay fee
                        miner_fee = 0.00001m;
                    }
                }

                send_amount -= miner_fee; //Take the miner fee from the amount sent

                if (send_amount < 0)
                { //But 0 is ok
                    NebliDexNetLog("Miner fee is too high currently");
                    return null;
                }

                Decimal send_amount_dec = send_amount;

                //Convert to satoshi
                send_amount_dec = Decimal.Multiply(send_amount_dec, 100000000);

                //Convert this to BigInteger
                BigInteger satoshi_send_amount = new BigInteger(send_amount_dec);

                //Now we determine where to send the coins
                if (satoshi_send_amount >= dust_output)
                { //May not have a to_address if less than dust
                    tout = new TxOut()
                    {
                        Value = new Money(satoshi_send_amount),
                        ScriptPubKey = to_address.ScriptPubKey
                    };
                    tx.Outputs.Add(tout);
                }

                if (refund == true)
                {
                    //Set all the nSquences to zero as this is a hashlock transaction
                    for (int i = 0; i < tx.Inputs.Count; i++)
                    {
                        tx.Inputs[i].Sequence = 0; //This must be allowed to use checklocktimeverify
                    }
                }

                for (int i = 0; i < tx.Inputs.Count; i++)
                {
                    //Create the scriptsig for each input
                    Script redeem_script = new Script(redeemscript_string);
                    ScriptCoin redeem_coin = null;
                    if (utxo_values.Count == 0)
                    {
                        redeem_coin = new ScriptCoin(tx.Inputs[i].PrevOut, new TxOut(10000, redeem_script.PaymentScript), redeem_script);
                    }
                    else
                    {
                        redeem_coin = new ScriptCoin(tx.Inputs[i].PrevOut, new TxOut(new Money(utxo_values[i]), redeem_script.PaymentScript), redeem_script);
                    }
                    TransactionSignature sig = tx.SignInput(priv_key.PrivateKey, redeem_coin); //Sign the transaction
                    Script p2pkh_script = PayToPubkeyHashTemplate.Instance.GenerateScriptSig(sig, priv_key.PrivateKey.PubKey); //Get the scriptsig that represents a typical p2pkh
                    Script final_scriptsig = null;
                    if (refund == false)
                    {
                        //We are paying to my address, utilize the secret
                        Op secret_bytes = Op.GetPushOp(ConvertHexStringToByteArray(secret));
                        //Script sig: Signature Pubkey Secret OP_1 Redeem_Script
                        final_scriptsig = p2pkh_script + secret_bytes + OpcodeType.OP_1 + Op.GetPushOp(redeem_script.ToBytes()); //Redeem script is initially push data
                    }
                    else
                    {
                        //Failed payment, get refund after certain time
                        //Script sig: Signature Pubkey OP_0 Redeem_Script
                        final_scriptsig = p2pkh_script + OpcodeType.OP_0 + Op.GetPushOp(redeem_script.ToBytes()); //Redeem script is initially push data
                    }
                    tx.Inputs[i].ScriptSig = final_scriptsig; //Already signed scriptsig
                }

                return tx; //This will return the transaction to be broadcasted

            }
            catch (Exception e)
            {
                NebliDexNetLog("Error while creating swap transaction: " + e.ToString());
            }
            return null;
        }
      
        public static void AddMyRecentTrade(int market, int ordertype, decimal price, decimal amount, string txhash, int pending)
        {
            //Add the transaction to the Sqlite database for historical purposes
            string myquery;
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            int utctime = UTCTime();

            myquery = "Insert Into MYTRADEHISTORY (utctime, market, type, price, amount, txhash, pending)";
            myquery += " Values (@time, @market, @typ, @pri, @amo, @txhash, @pend);";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", utctime);
            statement.Parameters.AddWithValue("@market", market);
            statement.Parameters.AddWithValue("@typ", ordertype);
            statement.Parameters.AddWithValue("@pri", price.ToString(CultureInfo.InvariantCulture));
            statement.Parameters.AddWithValue("@amo", amount.ToString(CultureInfo.InvariantCulture));
            statement.Parameters.AddWithValue("@txhash", txhash);
            statement.Parameters.AddWithValue("@pend", pending);
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();

            string format_date;
            string format_type;
            string format_market = "";
            if (pending == 0)
            {
                format_date = App.UTC2DateTime(utctime).ToString("yyyy-MM-dd");
            }
            else if (pending == 1)
            {
                format_date = "PENDING";
            }
            else
            {
                format_date = "CANCELLED";
            }
            if (Convert.ToInt32(ordertype) == 0)
            {
                format_type = "BUY";
            }
            else
            {
                format_type = "SELL";
            }
            format_market = App.MarketList[market].format_market;

			if (main_window_loaded == false) { return; }
            //Update the window as well with this trade info
            Application.Invoke(delegate
            {
                //Put this recent trade at the beginning
                main_window.Trade_History_List_Public.NodeStore.AddNode(new MyTrade { Date = format_date, Pair = format_market, Type = format_type, Price = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", price), Amount = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount), TxID = txhash }, 0);
                //Now scroll the view to the top
                main_window.Trade_History_List_Public.ScrollToCell(new TreePath(new int[] { 0 }), null, false, 0, 0);
            });
        }

        public static decimal GetMyRecentTradeAmount(string txhash)
        {
            //This method will return the trade amount from the recent trade, used by maker         
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            string myquery = "Select amount From MYTRADEHISTORY Where txhash = @hash";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@hash", txhash);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            bool dataavail = statement_reader.Read();
            decimal amount = 0;
            if (dataavail == true)
            {
                amount = Convert.ToDecimal(statement_reader["amount"].ToString(), CultureInfo.InvariantCulture);
            }
            statement_reader.Close();
            statement.Dispose();
            mycon.Close();
            return amount;
        }

        public static void UpdateMyRecentTrade(string txhash, int pending)
        {
            //Change from pending to completed, thus show the date
            string myquery;
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            int utctime = UTCTime();

            myquery = "Update MYTRADEHISTORY Set pending = @pend Where txhash = @hash";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@hash", txhash);
            statement.Parameters.AddWithValue("@pend", pending);
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();

            string format_date = "";
            if (pending == 0)
            {
                format_date = App.UTC2DateTime(utctime).ToString("yyyy-MM-dd");
            }
            else if (pending == 1)
            {
                format_date = "PENDING";
            }
            else
            {
                format_date = "CANCELLED";
            }

			if (main_window_loaded == false) { return; }
            //Update the window as well with this trade info
            Application.Invoke(delegate
            {
                //Put this recent trade at the beginning
                TreeModel model = App.main_window.Trade_History_List_Public.Model;
                TreeIter iter;
                TreePath path;
                int count = 0;

                if (model.GetIterFirst(out iter))
                {
                    //There is something here
                    do
                    {
                        path = new TreePath(new int[] { count }); //Horrible waste of memory but no other option
                        MyTrade my_recent = (MyTrade)App.main_window.Trade_History_List_Public.NodeStore.GetNode(path);
                        path.Dispose();
                        if (my_recent.TxID == txhash)
                        {
                            //Remove this old order and insert the new one at the top
                            App.main_window.Trade_History_List_Public.NodeStore.RemoveNode(my_recent);
                            App.main_window.Trade_History_List_Public.NodeStore.AddNode(new MyTrade { Date = format_date, Pair = my_recent.Pair, Type = my_recent.Type, Price = my_recent.Price, Amount = my_recent.Amount, TxID = my_recent.TxID }, 0);
                            break; //Exit the loop
                        }
                    } while (model.IterNext(ref iter));
                }
            });
        }

        public static void AddMyCNFee(int market, decimal fee)
        {
            //This adds the fee to the database for the CN and into the table
            string myquery;
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            int utctime = UTCTime();

            myquery = "Insert Into CNFEES (utctime, market, fee)";
            myquery += " Values (@time, @market, @myfee);";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", utctime);
            statement.Parameters.AddWithValue("@market", market);
            statement.Parameters.AddWithValue("@myfee", fee.ToString(CultureInfo.InvariantCulture));
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();

            string format_date;
            string format_market = "";
            format_date = App.UTC2DateTime(utctime).ToString("yyyy-MM-dd");
            format_market = App.MarketList[market].format_market;

			if (main_window_loaded == false) { return; }
            //Update the window as well with this trade info
            Application.Invoke(delegate
            {
                //Add the fee to the beginning of the list
                App.main_window.CN_Tx_List_Public.NodeStore.AddNode(new MyCNFee { Date = format_date, Pair = format_market, Fee = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", fee) }, 0);
            });
        }

        public static void ClearAllCNFees()
        {
            //This function is a user function to remove all the fees listed
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            string myquery = "Delete From CNFEES";
            SqliteCommand statement = new SqliteCommand(myquery, mycon);
            statement.ExecuteNonQuery();
            statement.Dispose();

			if (main_window_loaded == false) { return; }
            //Update the window as well with this CN info
            Application.Invoke(delegate
            {
                //Put this recent trade at the beginning
                App.main_window.CN_Tx_List_Public.NodeStore.Clear();
            });
        }

        public static void AddMyTxToDatabase(string txhash, string fro, string to, decimal amt, int wallet, int type, int reqtime, string order_nonce = "")
        {
            //Add the transaction to the Sqlite database for historical purposes
            string myquery;
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Add a row
            //Transaction type 2 is withdraw or fee transaction to validation node
            //Transaction type 0 is taker transaction to contract, to_add is my contract add
            //Transaction type 1 is maker transaction to contract, to_add is my contract add
            //Transaction type 3 is cancelled, type 4 is closed
            //Transaction type 5 is maker to taker contract, waiting for taker payment
            //Transaction type 6 is taker to maker transaction, pending for canceling (will cancel when time is up)

            //Atomic Transactions utilize:
            //utctime, txhash, from_add, to_add (contract add), cointype (of contract), amount (sent), type (of transaction), order_nonce_ref, req_utctime, waittime
            //custodial_redeemscript_add (of the other contract), counterparty_cointype (of the other contract), atomic_unlock_time* (of my contract), atomic_refund_time* (of my contract)
            //receive_amount* (from other contract), custodial_redeemscript (of the other contract), to_add_redeemscript* (of my contract), atomic_secret_hash*, atomic_secret*

            myquery = "Insert Into MYTRANSACTIONS (utctime, txhash, from_add, to_add, cointype, amount, custodial_redeemscript_add, type, waittime, order_nonce_ref, req_utctime_ref)";
            myquery += " Values (@time, @hash, @from, @to, @coin, @amt, '', @type, @time, @nonce, @reqtime);";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", UTCTime());
            statement.Parameters.AddWithValue("@hash", txhash);
            statement.Parameters.AddWithValue("@from", fro);
            statement.Parameters.AddWithValue("@to", to);
            statement.Parameters.AddWithValue("@coin", wallet);
            statement.Parameters.AddWithValue("@amt", amt);
            statement.Parameters.AddWithValue("@type", type);
            statement.Parameters.AddWithValue("@reqtime", reqtime);
            statement.Parameters.AddWithValue("@nonce", order_nonce);
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
        }

        public static string GetMyTransactionData(string field, int time, string order_nonce)
        {
            //This will return data from the field matching the time and order_nonce
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select all the rows from tradehistory
            string myquery = "Select " + field + " From MYTRANSACTIONS Where req_utctime_ref = @time And order_nonce_ref = @nonce And type != 2 And type != 3 And type != 4";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", time);
            statement.Parameters.AddWithValue("@nonce", order_nonce);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            bool dataavail = statement_reader.Read();
            string data = "";
            if (dataavail == true)
            {
                data = statement_reader[field].ToString();
            }
            statement_reader.Close();
            statement.Dispose();
            mycon.Close();
            return data;
        }

        public static bool SetMyTransactionData(string field, string val, int time, string order_nonce)
        {
            //This will return data from the field matching the time and order_nonce
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select all the rows from tradehistory
            string myquery = "Update MYTRANSACTIONS Set " + field + " = @val Where req_utctime_ref = @time And order_nonce_ref = @nonce And type != 2 And type != 3 And type != 4";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@val", val);
            statement.Parameters.AddWithValue("@time", time);
            statement.Parameters.AddWithValue("@nonce", order_nonce);
            int rows = statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
            if (rows > 0)
            {
                return true;
            }
            NebliDexNetLog("Unable to save :" + field + " value: " + val + " to database");
            return false;
        }

        public static bool SetMyTransactionData(string field, long val, int time, string order_nonce)
        {
            //This will return data from the field matching the time and order_nonce
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select all the rows from tradehistory
			string myquery = "Update MYTRANSACTIONS Set " + field + " = @val Where req_utctime_ref = @time And order_nonce_ref = @nonce And type != 2 And type != 3 And type != 4";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@val", val);
            statement.Parameters.AddWithValue("@time", time);
            statement.Parameters.AddWithValue("@nonce", order_nonce);
            int rows = statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
            if (rows > 0)
            {
                return true;
            } //If there was something to update
            NebliDexNetLog("Unable to save :" + field + " value: " + val + " to database");
            return false;
        }
              
        public static bool CancelFeeWithdrawalMonitor(int time, string order_nonce)
        {
            //This cancel monitoring the transaction associated with the trade fee
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select all the rows from tradehistory
            string myquery = "Update MYTRANSACTIONS Set type = 3 Where req_utctime_ref = @time And order_nonce_ref = @nonce And type = 2";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", time);
            statement.Parameters.AddWithValue("@nonce", order_nonce);
            int rows = statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
            if (rows > 0)
            {
                return true;
            } //If there was something to update
            NebliDexNetLog("Unable to save to database");
            return false;
        }

		public static void CheckMyTransactions()
        {
            //This function goes through the all the transactions and checks for activity or none
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Transaction type 2 is withdraw or fee transaction to validation node
            //Transaction type 0 is taker transaction to contract, to_add is my contract add
            //Transaction type 1 is maker transaction to contract, to_add is my contract add
            //Transaction type 3 is cancelled, type 4 is closed
            //Transaction type 5 is maker waiting for taker contract to pay
            //Transaction type 6 is taker waiting for taker contract to expire (pending close)

            //Select all the rows from mytransaction
            string myquery = "Select nindex, txhash, from_add, to_add, cointype, amount, custodial_redeemscript_add, type, order_nonce_ref, counterparty_cointype, to_add_redeemscript,";
            myquery += " req_utctime_ref, waittime, utctime, atomic_unlock_time, atomic_refund_time, atomic_secret_hash, atomic_secret, receive_amount, custodial_redeemscript From MYTRANSACTIONS Where type != 3 And type != 4";
            statement = new SqliteCommand(myquery, mycon);
            SqliteDataReader statement_reader = statement.ExecuteReader();

            DataTable table = new DataTable();
            table.Load(statement_reader); //Loads all the data in the table
            statement_reader.Close();
            statement.Dispose();

            //Now work on the data
            for (int i = 0; i < table.Rows.Count; i++)
            {
                int type = Convert.ToInt32(table.Rows[i]["type"]);
                int cointype = Convert.ToInt32(table.Rows[i]["cointype"]);
                int tx_waittime = Convert.ToInt32(table.Rows[i]["waittime"].ToString());

                //Check the blockchain to see if this has been confirmed
                try
                {

                    if (type == 0)
                    {
                        //Taker has paid to taker contract and is now waiting for maker to pay to its contract

                        UpdateWalletStatus(cointype, 2); //Lock taker account from sending

                        if (UTCTime() - tx_waittime > 30)
                        { //Been more than 30 seconds since checked this transaction
                            int maker_cointype = Convert.ToInt32(table.Rows[i]["counterparty_cointype"].ToString());
                            string maker_contract_add = table.Rows[i]["custodial_redeemscript_add"].ToString();
                            int contract_locktime = Convert.ToInt32(table.Rows[i]["atomic_unlock_time"].ToString());
                            int maker_inclusion_time = Convert.ToInt32(GetBlockInclusionTime(maker_cointype, 0));
                            int canceltime = contract_locktime - maker_inclusion_time - max_transaction_wait / 2; //Will cancel the waiting if waited past maker locktime

                            int blockchain_type = GetWalletBlockchainType(maker_cointype);
                            decimal expected_balance = Convert.ToDecimal(table.Rows[i]["receive_amount"].ToString(), CultureInfo.InvariantCulture);

                            bool balance_ok = false;
                            bool receiving_eth = false;

                            if (blockchain_type != 6)
                            {
								NebliDexNetLog("Waiting for maker contract " + maker_contract_add + " to fund");
                                decimal balance = 0;
                                if (maker_contract_add.Length > 0)
                                {
                                    balance = GetBlockchainAddressBalance(maker_cointype, maker_contract_add, false);
                                }
                                if (balance >= expected_balance)
                                {
                                    //This is good, maker has paid, now check if fees are ok
                                    balance_ok = true;
                                    decimal estimate_fee = blockchain_fee[blockchain_type] * 0.35m; //This is the fee we expect to pay for transfer
                                    if (blockchain_type == 0)
                                    {
                                        //Neblio fee has to be in multiples of 10,000 satoshi (0.0001)
                                        //Per Eddy, this value is rounded up
                                        estimate_fee = Math.Ceiling(estimate_fee / 0.0001m) * 0.0001m;
                                    }

                                    bool wallet_ntp1 = IsWalletNTP1(maker_cointype);
                                    if (wallet_ntp1 == true)
                                    {
                                        //This is a neblio token trade so check the neblio fees
                                        decimal base_balance = GetBlockchainAddressBalance(0, maker_contract_add, false);
                                        if (estimate_fee < 0.0002m) { estimate_fee = 0.0002m; } //We need balance to pay for op_return and miner fee
                                        if (base_balance - estimate_fee < 0)
                                        {
                                            NebliDexNetLog("Maker contract balance not enough to pay for fees");
                                            balance_ok = false;
                                        }
                                    }
                                    else
                                    {
										//Make the fee calculation more flexible as some clients assume different fees
                                        estimate_fee = estimate_fee / 10m;

                                        if (balance - expected_balance - estimate_fee < 0)
                                        {
                                            //Not enough balance to cover amount and fee
                                            NebliDexNetLog("Maker contract balance not enough to pay for fees");
                                            balance_ok = false;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                //We will check the Ethereum contract for a balance
								string secret_hash = table.Rows[i]["atomic_secret_hash"].ToString();
								if (Wallet.CoinERC20(maker_cointype) == false)
                                {
                                    NebliDexNetLog("Waiting for maker to pay Ethereum contract");
                                    balance_ok = VerifyBlockchainEthereumAtomicSwap(secret_hash, expected_balance, GetWalletAddress(maker_cointype), canceltime);
                                }
                                else
                                {
                                    //This is an ERC20 token
                                    NebliDexNetLog("Waiting for maker to pay ERC20 contract");
                                    balance_ok = VerifyERC20AtomicSwap(secret_hash, expected_balance, GetWalletAddress(maker_cointype), canceltime, maker_cointype);
                                }
                                receiving_eth = true;
                            }

                            myquery = "Update MYTRANSACTIONS Set waittime = @time Where nindex = @index;";
                            if (UTCTime() > canceltime)
                            {
                                //Waiting too long, cancel this trade and wait for our locktime
                                //Wallet will be available again to use however trade status will still remains as pending
                                myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 6 Where nindex = @index;";
                                UpdateWalletStatus(cointype, 0); //Unlock taker account from sending
                            }
                            else if (balance_ok == true)
                            {
                                //Maker has paid and with enough fees as well
                                //Try to pull the balance from the contract using the secret                        
                                string secret = table.Rows[i]["atomic_secret"].ToString();
                                //The secret will be used to redeem
                                Transaction tx = null;
                                Nethereum.Signer.TransactionChainId eth_tx = null;
                                if (receiving_eth == false)
                                {
                                    string my_redeem_add = GetWalletAddress(maker_cointype);
                                    string redeem_script_string = table.Rows[i]["custodial_redeemscript"].ToString();
                                    tx = CreateAtomicSwapP2SHTx(maker_cointype, maker_contract_add, my_redeem_add, redeem_script_string, 0, secret, false);
                                }
                                else
                                {
                                    //Redeeming from the maker's ethereum contract
                                    string secret_hash = table.Rows[i]["atomic_secret_hash"].ToString();
                                    string redeem_data = GenerateEthereumAtomicSwapRedeemData(secret_hash, secret);
									string eth_contract = ETH_ATOMICSWAP_ADDRESS;
                                    if (Wallet.CoinERC20(maker_cointype) == true)
                                    {
                                        eth_contract = ERC20_ATOMICSWAP_ADDRESS;
                                    }
                                    eth_tx = CreateSignedEthereumTransaction(maker_cointype, eth_contract, 0, false, 2, redeem_data);
                                }
                                if (tx != null || eth_tx != null)
                                {
                                    //Now broadcast this transaction
                                    bool timeout;
                                    bool broadcast_ok = true;
                                    string txhash;
									string calculated_txhash;
                                    if (tx != null)
                                    {
                                        txhash = TransactionBroadcast(maker_cointype, tx.ToHex(), out timeout);
										calculated_txhash = tx.GetHash().ToString();
                                        if (txhash.ToLower().Equals(calculated_txhash.ToLower()) == false)
                                        {
                                            NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                            txhash = "";
                                        }
                                    }
                                    else
                                    {
                                        txhash = TransactionBroadcast(maker_cointype, eth_tx.Signed_Hex, out timeout);
										calculated_txhash = eth_tx.HashID;
                                        if (txhash.ToLower().Equals(calculated_txhash.ToLower()) == false)
                                        {
                                            NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                            txhash = "";
                                        }
                                    }
                                    if (txhash.Length == 0 || timeout == true)
                                    {
                                        broadcast_ok = false; //Unable to broadcast transaction
                                        NebliDexNetLog("Failed to redeem funds from maker contract");
                                    }
                                    if (broadcast_ok == true)
                                    {
                                        NebliDexNetLog("Maker contract " + maker_contract_add + " paid, pulling funds");
                                        //Close this completely, trade was successful, now maker has to act
                                        myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 4 Where nindex = @index;";
                                        UpdateWalletStatus(cointype, 0); //Unlock taker account from sending        
                                        string tradehx_index = table.Rows[i]["txhash"].ToString();
                                        UpdateMyRecentTrade(tradehx_index, 0); //Close the trade                            
                                    }
                                }
                            }

                            //Update the database
                            statement = new SqliteCommand(myquery, mycon);
                            statement.Parameters.AddWithValue("@index", table.Rows[i]["nindex"].ToString());
                            statement.Parameters.AddWithValue("@time", UTCTime());
                            statement.ExecuteNonQuery();
                            statement.Dispose();

                        }

                    }
                    else if (type == 5)
                    {
						//Maker is waiting for the taker to pay to its contract, including fees
                        UpdateWalletStatus(cointype, 2); //Should not be available to use
                                                         //Once balance is there, maker will broadcast to its contract and wait for taker to pull from it

                        if (UTCTime() - tx_waittime > 30)
                        { //Been more than 30 seconds since checked this transaction

                            int taker_cointype = Convert.ToInt32(table.Rows[i]["counterparty_cointype"].ToString());
                            int blockchain_type = GetWalletBlockchainType(taker_cointype);
                            int canceltime = Convert.ToInt32(table.Rows[i]["atomic_unlock_time"].ToString()); //Will cancel if taker doesn't pay in time
                            decimal expected_balance = Convert.ToDecimal(table.Rows[i]["receive_amount"].ToString(), CultureInfo.InvariantCulture);

                            //This is information stored in cases of failed broadcast
                            //Will attempt to rebroadcast same hex if not sure if broadcast was successful
                            string maker_txinfo = table.Rows[i]["txhash"].ToString(); //This should normally be an empty string                         
                            string maker_txhash = "";
                            string maker_txhex = "";
                            int maker_txconf = 0;
                            if (maker_txinfo.Length > 0)
                            {
                                JObject txinfo = JObject.Parse(maker_txinfo);
                                maker_txhex = txinfo["hex"].ToString();
                                maker_txhash = txinfo["hash"].ToString();
                                maker_txconf = TransactionConfirmations(cointype, maker_txhash);
                            }

                            bool balance_ok = false;

                            if (blockchain_type != 6)
                            {
                                string taker_contract_add = table.Rows[i]["custodial_redeemscript_add"].ToString();
                                decimal balance = GetBlockchainAddressBalance(taker_cointype, taker_contract_add, false);
                                NebliDexNetLog("Waiting for taker contract " + taker_contract_add + " to fund");

                                if (balance >= expected_balance)
                                {
                                    //This is good, taker has paid, now check if fees are ok
                                    balance_ok = true;

                                    decimal estimate_fee = blockchain_fee[blockchain_type] * 0.35m; //This is the fee we expect to pay for transfer
                                    if (blockchain_type == 0)
                                    {
                                        //Neblio fee has to be in multiples of 10,000 satoshi (0.0001)
                                        //Per Eddy, this value is rounded up
                                        estimate_fee = Math.Ceiling(estimate_fee / 0.0001m) * 0.0001m;
                                    }

                                    bool wallet_ntp1 = IsWalletNTP1(taker_cointype);
                                    if (wallet_ntp1 == true)
                                    {
                                        //This is a neblio token trade so check the neblio fees
                                        decimal base_balance = GetBlockchainAddressBalance(0, taker_contract_add, false);
                                        if (estimate_fee < 0.0002m) { estimate_fee = 0.0002m; } //We need balance to pay for op_return and miner fee
                                        if (base_balance - estimate_fee < 0)
                                        {
                                            NebliDexNetLog("Taker contract balance not enough to pay for fees");
                                            balance_ok = false;
                                        }
                                    }
                                    else
                                    {
                                        //Make the fee calculation more flexible as some clients assume different fees
                                        estimate_fee = estimate_fee / 10m;

                                        if (balance - expected_balance - estimate_fee < 0)
                                        {
                                            //Not enough balance to cover amount and fee
                                            NebliDexNetLog("Taker contract balance not enough to pay for fees");
                                            balance_ok = false;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                //We will check the Ethereum contract for a balance as this is what we are receiving
                                NebliDexNetLog("Waiting for taker to pay to contract");
                                string secret_hash = table.Rows[i]["atomic_secret_hash"].ToString();
                                int maker_inclusion_time = Convert.ToInt32(GetBlockInclusionTime(cointype, 0)); //The time for my contract to use CLTV
                                int taker_canceltime = canceltime + max_transaction_wait / 2 + maker_inclusion_time; //This will be the taker's locktime
                                if (Wallet.CoinERC20(taker_cointype) == false)
                                {
                                    balance_ok = VerifyBlockchainEthereumAtomicSwap(secret_hash, expected_balance, GetWalletAddress(taker_cointype), taker_canceltime);
                                }
                                else
                                {
                                    balance_ok = VerifyERC20AtomicSwap(secret_hash, expected_balance, GetWalletAddress(taker_cointype), taker_canceltime, taker_cointype);
                                }
                            }

                            myquery = "Update MYTRANSACTIONS Set waittime = @time Where nindex = @index;";
                            if (maker_txconf == 0)
                            { //Maker transaction hasn't confirmed yet (probably not sent yet)
                                if (UTCTime() > canceltime)
                                {
                                    NebliDexNetLog("Maker closing trade due to lack of balance in taker contract. Maker also closing order.");
                                    //Waiting too long, cancel this trade completely and since we haven't sent anything to the contract, no worries
                                    //Wallet will be available again to use however trade status will still remains as pending
                                    myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 3, txhash = '' Where nindex = @index;";
                                    UpdateWalletStatus(cointype, 0); //Unlock maker account from sending
                                    string tradehx_index = table.Rows[i]["atomic_secret_hash"].ToString();
                                    UpdateMyRecentTrade(tradehx_index, 2); //Cancel this trade completely

                                    //For extra security, also close the order
                                    OpenOrder myord = null;
                                    lock (MyOpenOrderList)
                                    {
                                        for (int i2 = 0; i2 < MyOpenOrderList.Count; i2++)
                                        {
                                            if (MyOpenOrderList[i2].order_nonce == table.Rows[i]["order_nonce_ref"].ToString() && MyOpenOrderList[i2].is_request == false)
                                            {
                                                myord = MyOpenOrderList[i2];
                                                break;
                                            }
                                        }
                                    }
                                    if (myord != null)
                                    {
                                        CancelMyOrder(myord);
                                    }

                                }
                                else if (balance_ok == true)
                                {
                                    //Taker has paid and with enough fees as well
                                    NebliDexNetLog("Taker has funded taker contract, attempting to broadcast to maker contract");

                                    //Now put funds into maker contract
                                    string destination_add = table.Rows[i]["to_add"].ToString();
                                    decimal sendamount = Convert.ToDecimal(table.Rows[i]["amount"].ToString(), CultureInfo.InvariantCulture);

                                    decimal extra_neb = 0;
                                    int taker_blockchain = GetWalletBlockchainType(taker_cointype);
                                    int maker_blockchain = GetWalletBlockchainType(cointype);
                                    if (taker_blockchain != 0 && cointype == 3)
                                    {
                                        //I'm sending NDEX and taker is sending BTC or LTC, give extra neblio for spending
                                        extra_neb = blockchain_fee[0] * 12;
                                    }
                                    Transaction tx = null;
                                    Nethereum.Signer.TransactionChainId eth_tx = null;
                                    string calculated_txhash = "";
                                    if (maker_txhex.Length == 0)
                                    { //Only if maker hasn't tried to send transation before
                                        if (maker_blockchain != 6)
                                        {
                                            tx = CreateSignedP2PKHTx(cointype, sendamount, destination_add, false, true, "", 0, extra_neb, true);
                                        }
                                        else
                                        {
                                            //We are sending ethereum into the contract
                                            string secret_hash = table.Rows[i]["atomic_secret_hash"].ToString();
                                            if (Wallet.CoinERC20(cointype) == false)
                                            {
                                                string open_data = GenerateEthereumAtomicSwapOpenData(secret_hash, destination_add, canceltime); //With our parameters
                                                eth_tx = CreateSignedEthereumTransaction(cointype, ETH_ATOMICSWAP_ADDRESS, sendamount, true, 1, open_data);
                                            }
                                            else
                                            {
                                                //Sending ERC20 to contract, we already have this amount approved via approval transaction
                                                BigInteger int_sendamount = ConvertToERC20Int(sendamount, GetWalletERC20TokenDecimals(cointype));
                                                string token_contract = GetWalletERC20TokenContract(cointype);
                                                string open_data = GenerateERC20AtomicSwapOpenData(secret_hash, int_sendamount, token_contract, destination_add, canceltime);
                                                eth_tx = CreateSignedEthereumTransaction(cointype, ERC20_ATOMICSWAP_ADDRESS, sendamount, true, 1, open_data);
                                            }
                                        }
                                    }
                                    if (tx != null || eth_tx != null || maker_txhex.Length > 0)
                                    {
                                        //Prepare for transaction broadcast                                 
                                        int reqtime = Convert.ToInt32(table.Rows[i]["req_utctime_ref"].ToString());
                                        string txhex;
                                        if (tx != null)
                                        {
                                            calculated_txhash = tx.GetHash().ToString();
                                            txhex = tx.ToHex();
                                        }
                                        else if (eth_tx != null)
                                        {
                                            calculated_txhash = eth_tx.HashID;
                                            txhex = eth_tx.Signed_Hex;
                                        }
                                        else
                                        {
                                            calculated_txhash = maker_txhash;
                                            txhex = maker_txhex;
                                            //Resend the same data to a different node than last
                                        }
                                        //Temporarily store both the transaction hash and raw hex in this column as JObject
                                        JObject txinfo = new JObject();
                                        txinfo["hash"] = calculated_txhash;
                                        txinfo["hex"] = txhex;
                                        SetMyTransactionData("txhash", JsonConvert.SerializeObject(txinfo), reqtime, table.Rows[i]["order_nonce_ref"].ToString()); //Update txhash
                                        bool timeout;
                                        string txhash = TransactionBroadcast(cointype, txhex, out timeout);
										if (txhash.ToLower().Equals(calculated_txhash.ToLower()) == false)
                                        {
                                            NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                            txhash = "";
                                        }
                                        //Even if a power failure happens here, we will be able to switch over to monitoring tx on next boot up
                                        if (txhash.Length > 0)
                                        {
                                            //This was broadcasted ok
                                            NebliDexNetLog("Maker paid to maker contract: " + destination_add);
                                            //Now wait for spending transaction from maker contract
                                            myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 1, txhash = '" + calculated_txhash + "' Where nindex = @index;";

                                            UpdateWalletStatus(cointype, 2); //Lock maker funds as maker is now in contract 
                                        }
                                        else
                                        {
                                            NebliDexNetLog("Failed to broadcast transaction to maker contract, will try again.");
                                        }
                                    }
                                }
                            }
                            else if (maker_txconf > 0)
                            {
                                //Transaction to smart contract does exist, go directly to monitoring if taker is going to pull
                                NebliDexNetLog("Found maker payment transaction on the blockchain, despite failed broadcast. Monitor.");
                                myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 1, txhash = '" + maker_txhash + "' Where nindex = @index;";
                                UpdateWalletStatus(cointype, 2); //Lock maker funds as maker is now in contract
                            }

                            //Update the database
                            statement = new SqliteCommand(myquery, mycon);
                            statement.Parameters.AddWithValue("@index", table.Rows[i]["nindex"].ToString());
                            statement.Parameters.AddWithValue("@time", UTCTime());
                            statement.ExecuteNonQuery();
                            statement.Dispose();

                        }

                    }
                    else if (type == 1)
                    {
                        //Maker is now waiting for taker to pull funds from maker contract
                        UpdateWalletStatus(cointype, 2); //Should not be available to use

                        if (UTCTime() - tx_waittime > 30)
                        { //Been more than 30 seconds since checked this transaction
                            string secret_hash = table.Rows[i]["atomic_secret_hash"].ToString();
                            string maker_contract_add = table.Rows[i]["to_add"].ToString(); //Monitor our contract
                            if (GetWalletBlockchainType(cointype) == 6)
                            {
                                maker_contract_add = ETH_ATOMICSWAP_ADDRESS; //This is the location of the maker's contract
								if (Wallet.CoinERC20(cointype) == true)
                                {
                                    maker_contract_add = ERC20_ATOMICSWAP_ADDRESS; //This is location of maker's contract
                                }
                            }
                            string maker_txhash = table.Rows[i]["txhash"].ToString();
                            int conf = TransactionConfirmations(cointype, maker_txhash);

                            myquery = "Update MYTRANSACTIONS Set waittime = @time Where nindex = @index;";
                            if (conf >= 1)
                            {
                                NebliDexNetLog("Waiting for taker to pull from my contract: " + maker_contract_add);
                                //Our transaction has posted to the contract, wait for taker to pull balance
                                string secret = GetAtomicSwapSecret(cointype, maker_contract_add, secret_hash); //Continuously attempt to extract secret
                                if (secret.Length == 0)
                                {
                                    NebliDexNetLog("Unable to extract secret from contract: " + maker_contract_add);
                                    //Return balance if beyond refundtime                                                                   
                                    int refund_time = Convert.ToInt32(table.Rows[i]["atomic_refund_time"].ToString());
                                    if (UTCTime() > refund_time)
                                    {
                                        NebliDexNetLog("Refunding from maker contract as contract expired");
                                        int unlock_time = Convert.ToInt32(table.Rows[i]["atomic_unlock_time"].ToString());
                                        bool refunding_eth = false;
                                        if (GetWalletBlockchainType(cointype) == 6)
                                        {
                                            refunding_eth = true;
                                        }
                                        Transaction tx = null;
                                        Nethereum.Signer.TransactionChainId eth_tx = null;
                                        if (refunding_eth == false)
                                        {
                                            string return_add = table.Rows[i]["from_add"].ToString();
                                            string my_redeemscript_string = table.Rows[i]["to_add_redeemscript"].ToString();
                                            //Refund from our contract if unable to get secret                              
                                            tx = CreateAtomicSwapP2SHTx(cointype, maker_contract_add, return_add, my_redeemscript_string, (uint)unlock_time, "", true);
                                        }
                                        else
                                        {
                                            //Refund from ethereum contract using just secret_hash
                                            string refund_data = GenerateEthereumAtomicSwapRefundData(secret_hash);
											eth_tx = CreateSignedEthereumTransaction(cointype, maker_contract_add, 0, true, 3, refund_data);
                                        }
                                        if (tx != null || eth_tx != null)
                                        {
                                            //Now broadcast this transaction
                                            bool timeout;
                                            bool broadcast_ok = true;

                                            string txhash = "";
											string calculated_txhash = "";
                                            if (tx != null)
                                            {
                                                txhash = TransactionBroadcast(cointype, tx.ToHex(), out timeout);
												calculated_txhash = tx.GetHash().ToString();
                                                if (txhash.ToLower().Equals(calculated_txhash.ToLower()) == false)
                                                {
                                                    NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                                    txhash = "";
                                                }
                                            }
                                            else
                                            {
                                                txhash = TransactionBroadcast(cointype, eth_tx.Signed_Hex, out timeout);
												calculated_txhash = eth_tx.HashID;
                                                if (txhash.ToLower().Equals(calculated_txhash.ToLower()) == false)
                                                {
                                                    NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                                    txhash = "";
                                                }
                                            }
                                            if (txhash.Length == 0 || timeout == true)
                                            {
                                                broadcast_ok = false; //Unable to broadcast transaction
                                                NebliDexNetLog("Failed to refund funds from my contract");
                                            }
                                            if (broadcast_ok == true)
                                            {
                                                NebliDexNetLog("My contract " + maker_contract_add + " refund pulled");
                                                //Close this completely, trade failed
                                                myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 3 Where nindex = @index;";
                                                UpdateWalletStatus(cointype, 0); //Unlock maker account from sending        
                                                string tradehx_index = table.Rows[i]["atomic_secret_hash"].ToString();
                                                UpdateMyRecentTrade(tradehx_index, 2); //Cancel 

                                                //For extra security, also close the order
                                                OpenOrder myord = null;
                                                lock (MyOpenOrderList)
                                                {
                                                    for (int i2 = 0; i2 < MyOpenOrderList.Count; i2++)
                                                    {
                                                        if (MyOpenOrderList[i2].order_nonce == table.Rows[i]["order_nonce_ref"].ToString() && MyOpenOrderList[i2].is_request == false)
                                                        {
                                                            myord = MyOpenOrderList[i2];
                                                            break;
                                                        }
                                                    }
                                                }
                                                if (myord != null)
                                                {
                                                    CancelMyOrder(myord);
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    //Found the secret, now pull from taker contract right away
                                    bool receiving_eth = false;
                                    int taker_cointype = Convert.ToInt32(table.Rows[i]["counterparty_cointype"].ToString());
                                    //The secret will be used to redeem
                                    if (GetWalletBlockchainType(taker_cointype) == 6)
                                    {
                                        //We are redeeming eth
                                        receiving_eth = true;
                                    }
                                    Transaction tx = null;
                                    Nethereum.Signer.TransactionChainId eth_tx = null;
                                    string taker_contract_add = table.Rows[i]["custodial_redeemscript_add"].ToString();
                                    if (receiving_eth == false)
                                    {
                                        string taker_redeemscript_string = table.Rows[i]["custodial_redeemscript"].ToString();
                                        string my_redeem_add = GetWalletAddress(taker_cointype);
                                        tx = CreateAtomicSwapP2SHTx(taker_cointype, taker_contract_add, my_redeem_add, taker_redeemscript_string, 0, secret, false);
                                    }
                                    else
                                    {
                                        //All we need is the secret and the secret hash
                                        string redeem_data = GenerateEthereumAtomicSwapRedeemData(secret_hash, secret);
										eth_tx = CreateSignedEthereumTransaction(taker_cointype, taker_contract_add, 0, true, 2, redeem_data);
                                    }
                                    if (tx != null || eth_tx != null)
                                    {
                                        //Now broadcast this transaction
                                        bool timeout;
                                        bool broadcast_ok = true;
                                        string txhash;
										string calculated_txhash;
                                        if (tx != null)
                                        {
                                            txhash = TransactionBroadcast(taker_cointype, tx.ToHex(), out timeout);
											calculated_txhash = tx.GetHash().ToString();
                                            if(txhash.ToLower().Equals(calculated_txhash.ToLower()) == false){
                                                NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                                txhash = "";
                                            }
                                        }
                                        else
                                        {
                                            txhash = TransactionBroadcast(taker_cointype, eth_tx.Signed_Hex, out timeout);
											calculated_txhash = eth_tx.HashID;
                                            if (txhash.ToLower().Equals(calculated_txhash.ToLower()) == false)
                                            {
                                                NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                                txhash = "";
                                            }
                                        }
                                        if (txhash.Length == 0 || timeout == true)
                                        {
                                            broadcast_ok = false; //Unable to broadcast transaction
                                            NebliDexNetLog("Failed to redeem funds from taker contract");
                                        }
                                        if (broadcast_ok == true)
                                        {
                                            NebliDexNetLog("Secret found. Taker contract " + taker_contract_add + " paid, pulling funds");
                                            //Close this completely, trade was successful
                                            myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 4 Where nindex = @index;";
                                            UpdateWalletStatus(cointype, 0); //Unlock maker account from sending        
                                            string tradehx_index = table.Rows[i]["atomic_secret_hash"].ToString();
                                            UpdateMyRecentTrade(tradehx_index, 0); //Close the trade

                                            //Reshow order if necessary
                                            OpenOrder myord = null;
                                            lock (MyOpenOrderList)
                                            {
                                                for (int i2 = MyOpenOrderList.Count - 1; i2 >= 0; i2--)
                                                {
                                                    if (MyOpenOrderList[i2].order_nonce == table.Rows[i]["order_nonce_ref"].ToString() && MyOpenOrderList[i2].is_request == false)
                                                    {
                                                        myord = MyOpenOrderList[i2];
                                                        break;
                                                    }
                                                }
                                            }

                                            if (myord != null)
                                            {
                                                myord.order_stage = 0; //Available to trade again
                                                ExchangeWindow.ShowOrder(myord.order_nonce);
                                            }
                                        }
                                    }
                                }
                            }
                            else if (conf == 0)
                            {
                                NebliDexNetLog("Waiting for maker contract to have a balance");
                                int txtime = Convert.ToInt32(table.Rows[i]["utctime"].ToString()); //Time of transaction
                                if (UTCTime() - txtime > max_transaction_wait)
                                {
                                    //Our transaction did in fact fail to post
                                    NebliDexNetLog("Our transaction failed to broadcast, canceling trade");
                                    myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 3 Where nindex = @index;";
                                    UpdateWalletStatus(cointype, 0); //Unlock maker account from sending        
                                    string tradehx_index = table.Rows[i]["atomic_secret_hash"].ToString();
                                    UpdateMyRecentTrade(tradehx_index, 2); //Cancel
                                }
                            }

                            //Update the database
                            statement = new SqliteCommand(myquery, mycon);
                            statement.Parameters.AddWithValue("@index", table.Rows[i]["nindex"].ToString());
                            statement.Parameters.AddWithValue("@time", UTCTime());
                            statement.ExecuteNonQuery();
                            statement.Dispose();

                        }
                    }
                    else if (type == 6)
                    {
                        //Taker has canceled pulling from maker contract
                        //Taker will now wait for its contract to expire and pull funds back

                        if (UTCTime() - tx_waittime > 30)
                        { //Been more than 30 seconds since checked this transaction
                            int refund_time = Convert.ToInt32(table.Rows[i]["atomic_refund_time"].ToString()); //This is time we can refund from contract

                            myquery = "Update MYTRANSACTIONS Set waittime = @time Where nindex = @index;";
                            if (UTCTime() > refund_time)
                            {
                                //Time to return funds
                                NebliDexNetLog("Refunding from taker contract as contract has expired");
                                bool refunding_eth = false;
                                if (GetWalletBlockchainType(cointype) == 6)
                                {
                                    //Refunding eth
                                    refunding_eth = true;
                                }
                                decimal balance = 0;
                                bool close_ok = false;
                                string taker_contract_add = table.Rows[i]["to_add"].ToString(); //Monitor our contract
                                string taker_txhash = table.Rows[i]["txhash"].ToString(); //The hash that funded the contract
                                if (refunding_eth == false)
                                {
                                    balance = GetBlockchainAddressBalance(cointype, taker_contract_add, false);
                                }
                                else
                                {
                                    string secret_hash = table.Rows[i]["atomic_secret_hash"].ToString();
									if (Wallet.CoinERC20(cointype) == false)
                                    {
                                        balance = GetBlockchainEthereumAtomicSwapBalance(secret_hash);
                                    }
                                    else
                                    {
                                        balance = GetERC20AtomicSwapBalance(secret_hash, cointype);
                                    }
                                }
                                if (balance > 0)
                                {
                                    Transaction tx = null;
                                    Nethereum.Signer.TransactionChainId eth_tx = null;
                                    if (refunding_eth == false)
                                    {
                                        int unlock_time = Convert.ToInt32(table.Rows[i]["atomic_unlock_time"].ToString());
                                        string return_add = table.Rows[i]["from_add"].ToString();
                                        string my_redeemscript_string = table.Rows[i]["to_add_redeemscript"].ToString();
                                        tx = CreateAtomicSwapP2SHTx(cointype, taker_contract_add, return_add, my_redeemscript_string, (uint)unlock_time, "", true);
                                    }
                                    else
                                    {
                                        string secret_hash = table.Rows[i]["atomic_secret_hash"].ToString();
                                        string refund_data = GenerateEthereumAtomicSwapRefundData(secret_hash);
										eth_tx = CreateSignedEthereumTransaction(cointype, taker_contract_add, 0, true, 3, refund_data);
                                    }
                                    if (tx != null || eth_tx != null)
                                    {
                                        //Now broadcast this transaction
                                        bool timeout;
                                        bool broadcast_ok = true;
                                        string txhash;
										string calculated_txhash;
                                        if (tx != null)
                                        {
                                            txhash = TransactionBroadcast(cointype, tx.ToHex(), out timeout);
											calculated_txhash = tx.GetHash().ToString();
                                            if (txhash.ToLower().Equals(calculated_txhash.ToLower()) == false)
                                            {
                                                NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                                txhash = "";
                                            }
                                        }
                                        else
                                        {
                                            txhash = TransactionBroadcast(cointype, eth_tx.Signed_Hex, out timeout);
											calculated_txhash = eth_tx.HashID;
                                            if (txhash.ToLower().Equals(calculated_txhash.ToLower()) == false)
                                            {
                                                NebliDexNetLog("Calculated transaction hash failed to match returned hash");
                                                txhash = "";
                                            }
                                        }
                                        if (txhash.Length == 0 || timeout == true)
                                        {
                                            broadcast_ok = false; //Unable to broadcast transaction
                                            NebliDexNetLog("Failed to refund funds from my contract");
                                        }
                                        if (broadcast_ok == true)
                                        {
                                            NebliDexNetLog("My contract " + taker_contract_add + " refund pulled");
                                            close_ok = true;
                                        }
                                    }
                                }
                                else
                                {
                                    //Could not find a balance, in that case, only close if our transaction hasn't been posted either                       
                                    int conf = TransactionConfirmations(cointype, taker_txhash);
                                    if (conf == 0)
                                    {
                                        NebliDexNetLog("Nothing in contract address, closing trade");
                                        close_ok = true;
                                    }
                                }
                                if (close_ok == true)
                                {
                                    myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 3 Where nindex = @index;";
                                    UpdateWalletStatus(cointype, 0); //Unlock taker account from sending        
                                    UpdateMyRecentTrade(taker_txhash, 2); //Mark as canceled                    
                                }
                            }

                            //Update the database
                            statement = new SqliteCommand(myquery, mycon);
                            statement.Parameters.AddWithValue("@index", table.Rows[i]["nindex"].ToString());
                            statement.Parameters.AddWithValue("@time", UTCTime());
                            statement.ExecuteNonQuery();
                            statement.Dispose();

                        }
                    }
                    else if (type == 2)
                    {
                        //Make wallet wait by default if there is an out transaction not closed
                        UpdateWalletStatus(cointype, 2); //Should not be available to use
                        if (UTCTime() - tx_waittime > 30)
                        { //Been more than 30 seconds since checked this transaction

                            int conf = TransactionConfirmations(cointype, table.Rows[i]["txhash"].ToString());
                            bool available = false;

                            if (conf >= 1)
                            {
                                NebliDexNetLog("Outgoing transaction has been confirmed: " + table.Rows[i]["txhash"].ToString());
                                //This transaction has been confirmed, change type to completed
                                available = true;
                                myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 4 Where nindex = @index;";
                            }
                            else
                            {
                                NebliDexNetLog("Waiting for outgoing transaction to confirm: " + table.Rows[i]["txhash"].ToString());
                                //Not confirmed
                                int txtime = Convert.ToInt32(table.Rows[i]["utctime"].ToString()); //Time of transaction
                                if (UTCTime() - txtime > max_transaction_wait && conf == 0)
                                { //3 hours of being unconfirmed
                                  //This transaction has been unconfirmed for too long, cancel it
                                    available = true;
                                    myquery = "Update MYTRANSACTIONS Set waittime = @time, type = 3 Where nindex = @index;"; //TX checking cancelled
                                }
                                else
                                {
                                    myquery = "Update MYTRANSACTIONS Set waittime = @time Where nindex = @index;";
                                }
                            }

                            //Update the database
                            statement = new SqliteCommand(myquery, mycon);
                            statement.Parameters.AddWithValue("@index", table.Rows[i]["nindex"].ToString());
                            statement.Parameters.AddWithValue("@time", UTCTime());
                            statement.ExecuteNonQuery();
                            statement.Dispose();

                            //Update the wallet
                            if (available == true)
                            {
                                UpdateWalletStatus(cointype, 0); //Available
                            }
                            else
                            {
                                UpdateWalletStatus(cointype, 2); //Wait
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Transaction Database error: " + e.ToString());
                }
            }
            mycon.Close();
        }

		public static void CheckValidatingTransactions()
        {
            //This function will check all the validating transactions
            //0 status = just started validating
            //1 status = waiting for taker to fund taker contract
            //2 status = taker funded contract, waiting for maker to pull
            //3 status = validation closed, maker or taker pulled funds from taker contract
            //4 status = cancelled, usually means maker failed to pull funds from taker contract within 3 hours (max_transaction_wait)

            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Now get the total number of rows that we are validating transactions, including ones that haven't been sent yet
            //Will not include transactions older than 3 hours due to fact that audit nodes probably sent redeem script
            string my_pubkey = GetWalletPubkey(3);
            statement = new SqliteCommand("Select Count(nindex) From VALIDATING_TRANSACTIONS Where status < 3 And validating_cn_pubkey = @mypubkey And claimed = 0 And utctime > @backtime", mycon);
            statement.CommandType = CommandType.Text;
            statement.Parameters.AddWithValue("@mypubkey", my_pubkey);
            statement.Parameters.AddWithValue("@backtime", UTCTime() - 60 * 60 * 3);
            int new_cn_num_validating_tx = Convert.ToInt32(statement.ExecuteScalar().ToString());
            if (cn_num_validating_tx != new_cn_num_validating_tx)
            {
                if (run_headless == true)
                {
                    Console.WriteLine("Current Transactions Validating: " + new_cn_num_validating_tx);
                }
            }
            cn_num_validating_tx = new_cn_num_validating_tx;
            statement.Dispose();

            //Select all the rows from validation that we are responsible for validating
            string myquery = "Select nindex, status, redeemscript_add, market, reqtype, order_nonce_ref, utctime";
            myquery += " From VALIDATING_TRANSACTIONS Where status < 3 And validating_cn_pubkey = @mypubkey And waittime < @backtime";
            //Only query for rows that require active validation
            //It will only load transactions that haven't been checked in 30 seconds
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@mypubkey", my_pubkey);
            statement.Parameters.AddWithValue("@backtime", UTCTime() - 30);
            SqliteDataReader statement_reader = statement.ExecuteReader();

            DataTable table = new DataTable();
            table.Load(statement_reader); //Loads all the data in the table
            statement_reader.Close();
            statement.Dispose();

            //Now work on the data
            try
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    int status = Convert.ToInt32(table.Rows[i]["status"]);
                    int row_utctime = Convert.ToInt32(table.Rows[i]["utctime"]);

                    if (status == 0)
                    {
                        //Waiting for taker to finish the trade
                        NebliDexNetLog("Waiting For Taker to Agree to trade for: " + table.Rows[i]["order_nonce_ref"].ToString());

                        myquery = "Update VALIDATING_TRANSACTIONS Set waittime = @time Where nindex = @index;";
                        if (UTCTime() - row_utctime > 60 * 5)
                        {
                            //Cancel after five minutes in this status, shouldn't be here so long
                            NebliDexNetLog("Canceling waiting for taker to agree to trade for: " + table.Rows[i]["order_nonce_ref"].ToString());
                            //This status has been sitting for over 1 hr waiting for taker to send fees
                            //Cancel the trade
                            myquery = "Update VALIDATING_TRANSACTIONS Set waittime = @time, status = 4, taker_tx='', taker_feetx='', claimed = 1 Where nindex = @index;";

                            //This status will only usually happen if validator nodes disconnects before receiving taker response
                        }

                        statement = new SqliteCommand(myquery, mycon);
                        statement.Parameters.AddWithValue("@index", table.Rows[i]["nindex"].ToString());
                        statement.Parameters.AddWithValue("@time", UTCTime());
                        statement.ExecuteNonQuery();
                        statement.Dispose();
                    }
                    else if (status == 1 || status == 2)
                    {

                        //Waiting for an amount to arrive to taker contract
                        int market = Convert.ToInt32(table.Rows[i]["market"].ToString());
                        int reqtype = Convert.ToInt32(table.Rows[i]["reqtype"].ToString());
                        string redeem_add = table.Rows[i]["redeemscript_add"].ToString();

                        int redeemwallet = 0; //The type of wallet of the taker contract

                        bool checking_eth = false;
                        if (reqtype == 0)
                        {
                            //Taker buying trade
                            redeemwallet = MarketList[market].base_wallet;
                        }
                        else
                        {
                            //Taker selling trade
                            redeemwallet = MarketList[market].trade_wallet;
                        }

                        Decimal contract_balance = 0;
                        if (GetWalletBlockchainType(redeemwallet) != 6)
                        {
                            checking_eth = false;
                            contract_balance = GetBlockchainAddressBalance(redeemwallet, redeem_add, true);
                        }
                        else
                        {
                            //Check the balance of the ethereum contract using the taker secret hash
                            checking_eth = true;
							if (Wallet.CoinERC20(redeemwallet) == false)
                            {
                                contract_balance = GetBlockchainEthereumAtomicSwapBalance(redeem_add);
                            }
                            else
                            {
                                contract_balance = GetERC20AtomicSwapBalance(redeem_add, redeemwallet);
                            }
                        }
                        //Default update
                        myquery = "Update VALIDATING_TRANSACTIONS Set waittime = @time Where nindex = @index;";

                        if (UTCTime() - row_utctime > max_transaction_wait)
                        {
                            //Cancel transaction checking as no balance has been redeemed in last 3 hours
                            myquery = "Update VALIDATING_TRANSACTIONS Set waittime = @time, status = 4 Where nindex = @index;";
                            NebliDexNetLog("Validating trade cancelled due to timeout: " + redeem_add);
                            CNRequestCancelOrderNonce(market, table.Rows[i]["order_nonce_ref"].ToString());
                        }
                        else if (status == 1)
                        {
                            //Waiting for taker to fund
                            if (contract_balance > 0)
                            {
                                //Some funds have arrived at this contract, update the status
                                myquery = "Update VALIDATING_TRANSACTIONS Set waittime = @time, status = 2 Where nindex = @index;";
                            }
                            else
                            {
                                NebliDexNetLog("Waiting For Taker to Fund Contract Account: " + redeem_add);
                            }
                        }
                        else if (status == 2)
                        {
                            //Waiting for maker to pull
                            if (checking_eth == true)
                            {
                                //Continuously try to grab the secret from contract, once it is visible, the contract has paid to maker
								string secret = GetEthereumAtomicSwapSecret(redeem_add, Wallet.CoinERC20(redeemwallet));
                                if (secret.Length > 0)
                                {
                                    contract_balance = 0; //Secret was revealed so no balance is possibly there
                                }
                            }

                            if (contract_balance == 0)
                            {
                                //Maker or taker has pulled, trade is completely finished
                                myquery = "Update VALIDATING_TRANSACTIONS Set waittime = @time, status = 3 Where nindex = @index;";

                                //Reshow order
                                //Reshow the hidden order
                                JObject showjs = new JObject();
                                showjs["cn.method"] = "cn.relayshoworder";
                                showjs["cn.response"] = 0;
                                showjs["cn.result"] = table.Rows[i]["order_nonce_ref"].ToString();
                                ExchangeWindow.ShowOrder(table.Rows[i]["order_nonce_ref"].ToString());
                                RelayShowOrder(null, showjs);

                                //Maker will need to manually reactivate its own order when its ready
                                //Remove all the validation connections for this request
                                string order_nonce = table.Rows[i]["order_nonce_ref"].ToString();
                                lock (DexConnectionList)
                                {
                                    for (int i2 = 0; i2 < DexConnectionList.Count; i2++)
                                    {
                                        if (DexConnectionList[i2].outgoing == false && DexConnectionList[i2].contype == 4 && DexConnectionList[i2].open == true)
                                        {
                                            //Only send this message to taker
                                            if (DexConnectionList[i2].tn_connection_nonce == order_nonce && DexConnectionList[i2].tn_connection_time == row_utctime)
                                            {
                                                DexConnectionList[i2].open = false;
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                NebliDexNetLog("Waiting For Maker to pull from Taker Contract Account: " + redeem_add);
                            }
                        }

                        statement = new SqliteCommand(myquery, mycon);
                        statement.Parameters.AddWithValue("@index", table.Rows[i]["nindex"].ToString());
                        statement.Parameters.AddWithValue("@time", UTCTime());
                        statement.ExecuteNonQuery();
                        statement.Dispose();

                    }
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Validation Database error: " + e.ToString());
            }
            finally
            {
                mycon.Close();
                lastvalidate_time = UTCTime();
            }
        }

        public static string SendBlockHelperMessage(JObject msg, bool use_local)
        {
            //This method serialize a jobject and send a message to the blockhelper either on a local machine
            //or through a CN. The message in a serialized string will be returned

            //First find the appropriate dex connection
            DexConnection dex = null;
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].open == true)
                    {
                        if (use_local == true)
                        {
                            if (DexConnectionList[i].contype == 0)
                            {
                                dex = DexConnectionList[i];
                                break;
                            }
                        }
                        else
                        {
                            if (DexConnectionList[i].contype == 5)
                            {
                                dex = DexConnectionList[i];
                                break;
                            }
                        }
                    }
                }
            }

            if (msg == null) { return ""; }
            string msg_encoded = JsonConvert.SerializeObject(msg);
            if (msg_encoded.Length == 0) { return ""; }
            //Now add data length and send data
            string resp_string = "";
            try
            {
                if (use_local == false)
                {
                    //This means that the message will be sent to a critical node designated to query its blockhelper
                    //Which will then return the raw string back to the querying client which will then run it as blockdata
                    Byte[] data = System.Text.Encoding.ASCII.GetBytes(msg_encoded);
                    uint data_length = (uint)msg_encoded.Length;
                    lock (dex.blockhandle)
                    { //Prevents other threads from doing blocking calls to connection  
                        if (dex.open == false) { return ""; }
                        dex.blockhandle.Reset();
                        dex.blockdata = "";
                        dex.stream.Write(Uint2Bytes(data_length), 0, 4); //Write the length of the bytes to be received
                        dex.stream.Write(data, 0, data.Length);
                        //Now wait for the blockdata to return the raw string
                        dex.blockhandle.WaitOne(30000); //This will wait 30 seconds for a response
                        if (dex.blockdata == "")
                        {
                            //No data received so close connection
                            throw new Exception("Failed to receive data from CN blockhelper");
                        }
                        resp_string = dex.blockdata;
                    }
                }
                else
                {
                    //Using a local node
                    lock (dex) //Assure that only one thread is writing to this stream at a time
                    {
                        if (dex.open == false) { return ""; }
                        resp_string = CNWaitResponse(dex.stream, dex.client, msg_encoded);
                        dex.lasttouchtime = UTCTime();
                    }
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Failed to send blockhelper message. Connection Disconnected: " + dex.ip_address[0] + ", error: " + e.ToString());
                dex.open = false;
            }
            return resp_string;
        }

		public static string TransactionBroadcast(int cointype, string hex, out bool timeout)
        {
            timeout = false;
            //Will return the transaction hash id
            try
            {
                int connectiontype = GetWalletBlockchainType(cointype);
                if (connectiontype != 0)
                {
                    if (connectiontype == 6)
                    {
                        //Ethereum has its own broadcasting method
                        return EthereumTransactionBroadcast(hex, out timeout);
                    }
                    DexConnection dex = null;

                    lock (DexConnectionList)
                    {
                        for (int i2 = 0; i2 < DexConnectionList.Count; i2++)
                        {
                            if (DexConnectionList[i2].contype == 1 && DexConnectionList[i2].open == true && DexConnectionList[i2].blockchain_type == connectiontype)
                            {
                                dex = DexConnectionList[i2];
                                break;
                            }
                        }
                    }

                    if (dex == null) { timeout = true; return ""; } //No dex exists, do not even try to broadcast

                    string blockdata = "";
                    NebliDexNetLog("Sending Electrum Transaction: " + hex);
                    lock (dex.blockhandle)
                    { //Prevents other threads from doing blocking calls to connection
                        dex.blockhandle.Reset();
                        SendElectrumAction(dex, 5, hex);
                        dex.blockhandle.WaitOne(10000); //This will wait 10 seconds for a response
                        if (dex.blockdata == "") { timeout = true; return ""; }
                        blockdata = dex.blockdata;
                    }

					//Search for malicious responses from electrum server (source of major hack to electrum network)
					if (blockdata.IndexOf(">", StringComparison.InvariantCulture) >= 0 || blockdata.IndexOf(".com", StringComparison.InvariantCulture) >= 0 || blockdata.IndexOf("://", StringComparison.InvariantCulture) >= 0)
                    {
                        //There is a script/website here that is not supposed to be here
                        NebliDexNetLog("Transaction broadcast failed: Incidentally connected to malicious electrum server, disconnecting and removing");
                        RemoveElectrumServer(-1, dex.ip_address[0]); //Remove the IP address from our list
                        dex.open = false;
                        timeout = true; //Tell client tx failed to even broadcast
                        return ""; //Failed broadcast
                    }

                    NebliDexNetLog("Electrum transaction response: " + blockdata);
                    JObject result = JObject.Parse(blockdata);
                    if (result["result"] == null)
                    {
                        //No transaction ID
						dex.open = false; //Disconnect and reconnect, then try again
                        return ""; //Failed broadcast
                    }
                    return result["result"].ToString(); //Should be transaction ID hash
                }
                else
                {
                    //Neblio based transactions
                    string nodeurl = NEBLAPI_server;
                    NebliDexNetLog("Sending NTP1 Transaction: " + hex);
                    string response = "";
                    JObject result = null;
                    if (using_blockhelper == false)
                    {
                        if (using_cnblockhelper == false)
                        {
                            JObject request = new JObject();
                            request["txHex"] = hex;
                            response = HttpRequest(nodeurl + "/ntp1/broadcast", JsonConvert.SerializeObject(request), out timeout);
                            NebliDexNetLog("NTP Transaction response: " + response);
                            if (response.Length == 0) { return ""; }
                            result = JObject.Parse(response);
                            if (result["code"] != null) { return ""; } //This is also an error
                        }
                        else
                        {
                            //Post the transaction to a CN with the expectation it will broadcast it
                            JObject req = new JObject();
                            req["cn.method"] = "blockhelper.broadcasttx";
                            req["cn.response"] = 0;
                            req["cn.tx_hex"] = hex;
                            response = SendBlockHelperMessage(req, false);
                            NebliDexNetLog("NTP Transaction response: " + response);
                            if (response.Length == 0) { return ""; }
                            result = JObject.Parse(response);
                            if (result["cn.result"] == null)
                            {
                                //This means error
                                NebliDexNetLog("Unable to broadcast transaction, error: " + result["cn.error"].ToString());
                                return "";
                            }
                            result["txid"] = result["cn.result"];
                            result.Remove("cn.result");
                        }
                    }
                    else
                    {
                        //Post the transaction to our own node
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.broadcasttx";
                        req["rpc.response"] = 0;
                        req["rpc.tx_hex"] = hex;
                        response = SendBlockHelperMessage(req, true);
                        NebliDexNetLog("NTP Transaction response: " + response);
                        if (response.Length == 0) { return ""; }
                        result = JObject.Parse(response);
                        if (result["rpc.result"] == null)
                        {
                            //This means error
                            NebliDexNetLog("Unable to broadcast transaction, error: " + result["rpc.error"].ToString());
                            return "";
                        }
                        result["txid"] = result["rpc.result"];
                        result.Remove("rpc.result");
                    }
                    if (result["txid"] == null)
                    {
                        return ""; //Failed transaction
                    }
                    return result["txid"].ToString();
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Failed to broadcast transaction: " + e.ToString());
            }
            return "";
        }

		public static void GetNTP1Balances()
        {
            try
            {
                //This function is ran periodically and will grab all the NTP1 balances from an address
                string nebl_add = GetWalletAddress(0); //Base wallet address

                string nodeurl = NEBLAPI_server; //Currently only one node at this time
                bool timeout;
                string response = "";
                JObject result = null;
                if (using_blockhelper == false)
                {
                    if (using_cnblockhelper == false)
                    {
                        response = HttpRequest(nodeurl + "/ntp1/addressinfo/" + nebl_add, "", out timeout);
                        if (response.Length == 0)
                        {
                            ntp1downcounter++;
                            NebliDexNetLog("NTP1 API Server unavailable");
                            if (ntp1downcounter >= 3 && ntp1downcounter % 3 == 0)
                            {
                                if (critical_node == false)
                                {
                                    //Try to connect to a CN blockhelper
                                    //Client connects to API server unless there is an issue with the API connection
                                    ConnectCNBlockHelper();
                                    //Then check the sync to make sure it is up to date
                                    CheckCNBlockHelperServerSync();
                                }
                                else
                                {
                                    //Continually try to connect to the local blockhelper if API connection is down
                                    ConnectBlockHelperNode();
                                }
                            }
                            return;
                        }
                        result = JObject.Parse(response);
                        if (result["code"] != null) { return; } //This is also an error
                        ntp1downcounter = 0;
                    }
                    else
                    {
                        //Querying the CN for UTXO information
                        JObject req = new JObject();
                        req["cn.method"] = "blockhelper.getaddressinfo";
                        req["cn.response"] = 0;
                        req["cn.address"] = nebl_add;
                        response = SendBlockHelperMessage(req, false);
                        if (response.Length == 0)
                        {
                            //Failed to get a response in time
                            NebliDexNetLog("Failed to receive a response from the CN");
                            return;
                        }
                        result = JObject.Parse(response);
                        if (result["cn.result"] == null)
                        {
                            //This means error
                            NebliDexNetLog("Unable to acquire balance for neblio address " + nebl_add + ", error: " + result["cn.error"].ToString());
                            return;
                        }
                        result["utxos"] = result["cn.result"];
                        result.Remove("cn.result");
                    }
                }
                else
                {
                    //Querying our own blockhelper for information
                    JObject req = new JObject();
                    req["rpc.method"] = "rpc.getaddressinfo";
                    req["rpc.response"] = 0;
                    req["rpc.neb_address"] = nebl_add;
                    response = SendBlockHelperMessage(req, true);
                    if (response.Length == 0)
                    {
                        //Failed to get a response in time
                        NebliDexNetLog("Failed to receive a response from the blockhelper");
                        return;
                    }
                    result = JObject.Parse(response);
                    if (result["rpc.result"] == null)
                    {
                        //This means error
                        NebliDexNetLog("Unable to acquire balance for neblio address " + nebl_add + ", error: " + result["rpc.error"].ToString());
                        return;
                    }
                    result["utxos"] = result["rpc.result"];
                    result.Remove("rpc.result");
                }

                //We wil get the balances of each unspent and add them up
                for (int i = 0; i < WalletList.Count; i++)
                {
                    if (GetWalletBlockchainType(WalletList[i].type) != 0) { continue; } //Only do neblio based wallets
                    int wallet = WalletList[i].type;

                    string tokenid = "";
                    if (IsWalletNTP1(WalletList[i].type) == true)
                    {
                        //This is a token wallet
                        tokenid = GetWalletTokenID(wallet);
                        if (tokenid.Length == 0) { continue; } //Skip wallets that do not exist
                    }

                    //Go through each unspent for each token type
                    Decimal sat_amount = 0;
                    bool unconfirmed_exist = false;
                    int total_utxo = 0;

                    foreach (JToken utxo in result["utxos"])
                    {
                        //Find the tokens in the unspent
                        bool no_token = true;
                        int height = Convert.ToInt32(utxo["blockheight"].ToString());
                        foreach (JToken token in utxo["tokens"])
                        {
                            //There can be more than one token per utxo
                            no_token = false;
                            string id = token["tokenId"].ToString();
                            if (id.Equals(tokenid) == true)
                            {
                                //This is our desired token
                                if (height >= 0)
                                {
                                    sat_amount += Decimal.Parse(token["amount"].ToString());
                                }
                                else
                                {
                                    unconfirmed_exist = true;
                                }
                            }
                        }
                        if (wallet == 0 && no_token == true)
                        {
                            //This is a pure Neblio unspent output, add to wallet
                            if (height >= 0)
                            {
                                sat_amount += Decimal.Parse(utxo["value"].ToString());
                            }
                            else
                            {
                                unconfirmed_exist = true;
                            }
                        }
                        total_utxo++;
                        if (total_utxo > 1000) { break; }
                    }

                    if (wallet == 0)
                    {
                        sat_amount = Decimal.Divide(sat_amount, 100000000);
                    }

                    //Don't include value from unconfirmed transactions (can't use them)
                    if (unconfirmed_exist == false)
                    {
                        UpdateWalletBalance(wallet, sat_amount, 0);
                    }
                    else
                    {

                        int status = 0;
                        for (int i2 = 0; i2 < WalletList.Count; i2++)
                        {
                            if (WalletList[i2].type == wallet)
                            {
                                status = WalletList[i2].status; break;
                            }
                        }
                        if (status != 2)
                        { //Not waiting status
                          //Make sure wallet is not waiting, if not then change to pend
                            UpdateWalletBalance(wallet, sat_amount, 1);
                            UpdateWalletStatus(wallet, 1); //Balance pending
                        }
                    }

                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Failed to get Neblio balances: " + e.ToString());
            }
        }

        public static Tuple<Transaction, Decimal> GenerateTokenTransactionHex(int wallet, decimal amount, string from_add, string address, string val_add, decimal val_fee, bool from_multisig)
        {
            //This will generate a token transaction for the appropriate wallet
            //We also need to create an output to ourself for the change
            if (wallet == 0)
            {
                //If the wallet is 0 (sending NEBL), the amount is ignored and only the val_add and val_fee are used
                if (val_add.Length == 0 || val_fee <= 0) { return null; }
            }
            try
            {
                JObject request = new JObject(); //We will recreate our sendtoken request
                request["fee"] = 0; //We will handle the fees

                //Make sure change is split from normal change always
                JObject splitchange = new JObject();
                splitchange["splitChange"] = true;
                request["flags"] = splitchange;

                request["from"] = new JArray(from_add);
                JArray to_array = new JArray();
                if (wallet == 0)
                {
                    //And we are sending NEBL, this is the NDEX fee
                    //This transaction requires a fee
                    JObject to = new JObject();
                    to["address"] = val_add;
                    to["amount"] = Convert.ToInt32(val_fee);
                    if (Convert.ToInt32(val_fee) <= 0) { return null; } //Somehow, we have a 0 token amount

                    to["tokenId"] = GetWalletTokenID(3); //NDEX wallet
                    to_array.Add(to);

                    if (from_multisig == false)
                    {
                        //Multisig wallets do not have change
                        decimal fee_change = GetWalletAmount(3) - val_fee;
                        if (fee_change > 0)
                        {
                            //Send money back to me
                            to = new JObject();
                            to["address"] = from_add;
                            to["amount"] = Convert.ToInt32(fee_change);
                            to["tokenId"] = GetWalletTokenID(3); //NDEX wallet
                            to_array.Add(to);
                        }
                    }
                }
                else
                {
                    //We are sending a token, along witha fee
                    JObject to = new JObject();
                    to["address"] = address;
                    bool included_fee = false;
                    if (wallet == 3 && address.Equals(val_add) == true)
                    {
                        //We are sending NDEX to the redeem script or back to taker
                        included_fee = true;
                        amount += val_fee;
                        to["amount"] = Convert.ToInt32(amount);
                        if (Convert.ToInt32(amount) <= 0) { return null; }
                    }
                    else
                    {
                        to["amount"] = Convert.ToInt32(amount);
                        if (Convert.ToInt32(amount) <= 0) { return null; }
                        if (wallet == 3)
                        {
                            //Sending NDEX to another person, usually the validator
                            amount += val_fee;
                        }
                    }
                    to["tokenId"] = GetWalletTokenID(wallet);
                    to_array.Add(to);

                    //Now send the change back to the from_add
                    if (from_multisig == false)
                    {
                        //This will work for the multsig, ndex and trif tokens
                        decimal change_amount = GetWalletAmount(wallet) - amount; //If we have change
                        if (change_amount > 0)
                        {
                            //Send this token back to me
                            to = new JObject();
                            to["address"] = from_add;
                            to["amount"] = Convert.ToInt32(change_amount);
                            to["tokenId"] = GetWalletTokenID(wallet);
                            to_array.Add(to);
                        }
                    }

                    if (val_fee > 0 && included_fee == false)
                    {
                        //Some token transactions do not have a fee
                        to = new JObject();
                        to["address"] = val_add;
                        to["amount"] = Convert.ToInt32(val_fee);
                        to["tokenId"] = GetWalletTokenID(3); //NDEX wallet
                        to_array.Add(to);

                        if (wallet != 3 && from_multisig == false)
                        {
                            //NDEX is already incorporated in the previous out address
                            decimal fee_change = GetWalletAmount(3) - val_fee;
                            if (fee_change > 0)
                            {
                                //Send NDEX back to me
                                to = new JObject();
                                to["address"] = from_add;
                                to["amount"] = Convert.ToInt32(fee_change);
                                to["tokenId"] = GetWalletTokenID(3); //NDEX wallet
                                to_array.Add(to);
                            }
                        }
                    }

                }
                request["to"] = to_array;

                string json = JsonConvert.SerializeObject(request);
                NebliDexNetLog("Create local sendtoken request: " + json);

                //We are creating the ntp1 transaction locally instead of from the API server
                //The new method is to create the ntp1 transactions locally
                Decimal tx_input_values = 0;
                Transaction tx = GenerateScratchNTP1Transaction(from_add, to_array, ref tx_input_values);
				if (tx == null) { return null; }
                return Tuple.Create(tx, tx_input_values);
            }
            catch (Exception e)
            {
                NebliDexNetLog("Failed to create token transaction: " + e.ToString());
            }
            return null;
        }

		public static Decimal GetBlockchainAddressBalance(int cointype, string address, bool satoshi)
        {

            int connectiontype = GetWalletBlockchainType(cointype);
            if (connectiontype != 0)
            {
                //This will get the balance of an address based on unspent and return it
                //This is a blocking call
                //It will return the amount in satoshi for everything except tokens and ethereum
                if (connectiontype == 6)
                {
                    //This is an ethereum transaction
					return GetBlockchainEthereumBalance(address, cointype);
                }
                DexConnection dex = null;

                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].contype == 1 && DexConnectionList[i].open == true && DexConnectionList[i].blockchain_type == connectiontype)
                        {
                            dex = DexConnectionList[i];
                            break;
                        }
                    }
                }

                if (dex == null) { return 0; }

                try
                {
                    string blockdata = "";
                    lock (dex.blockhandle)
                    { //Prevents other threads from doing blocking calls to connection
                        dex.blockhandle.Reset();
                        string scripthash = GetElectrumScriptHash(address, cointype);
                        SendElectrumAction(dex, 9, scripthash);
                        dex.blockhandle.WaitOne(10000); //This will wait 10 seconds for a response
                        if (dex.blockdata == "")
                        {
                            NebliDexNetLog("No electrum response");
                            //Disconnect from server so it doesn't happen again
                            dex.open = false;
                            return 0;
                        } //No response
                        blockdata = dex.blockdata;
                    }
                    if (satoshi == true)
                    {
                        return Decimal.Parse(blockdata); //Will return a satoshi amount (very large)
                    }
                    else
                    {
                        Decimal bal = Decimal.Parse(blockdata);
                        return Decimal.Divide(bal, 100000000);
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to parse electrum data: " + e.ToString());
                }
                return 0;
            }
            else
            {
                try
                {
                    string nodeurl = NEBLAPI_server; //Currently only one node at this time
                    bool timeout;

                    string response = "";
                    JObject result = null;
                    if (using_blockhelper == false)
                    {
                        if (using_cnblockhelper == false)
                        {
                            response = HttpRequest(nodeurl + "/ntp1/addressinfo/" + address, "", out timeout);
                            if (response.Length == 0) { return 0; }
                            result = JObject.Parse(response);
                            if (result["code"] != null) { return 0; } //This is also an error
                        }
                        else
                        {
                            //Querying the CN for UTXO information
                            JObject req = new JObject();
                            req["cn.method"] = "blockhelper.getaddressinfo";
                            req["cn.response"] = 0;
                            req["cn.address"] = address;
                            response = SendBlockHelperMessage(req, false);
                            if (response.Length == 0) { return 0; }
                            result = JObject.Parse(response);
                            if (result["cn.result"] == null)
                            {
                                //This means error
                                NebliDexNetLog("Unable to acquire balance for neblio address " + address + ", error: " + result["cn.error"].ToString());
                                return 0;
                            }
                            result["utxos"] = result["cn.result"];
                            result.Remove("cn.result");
                        }
                    }
                    else
                    {
                        //Querying our own blockhelper for information
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.getaddressinfo";
                        req["rpc.response"] = 0;
                        req["rpc.neb_address"] = address;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0) { return 0; }
                        result = JObject.Parse(response);
                        if (result["rpc.result"] == null)
                        {
                            //This means error
                            NebliDexNetLog("Unable to acquire balance for neblio address " + address + ", error: " + result["rpc.error"].ToString());
                            return 0;
                        }
                        result["utxos"] = result["rpc.result"];
                        result.Remove("rpc.result");
                    }

                    //Now parse the data and get the value  
                    string tokenid = GetWalletTokenID(cointype);
                    int total_utxo = 0;
                    Decimal sat_amount = 0;
                    foreach (JToken utxo in result["utxos"])
                    {
                        //Find the tokens in the unspent
                        bool no_token = true;
                        int height = Convert.ToInt32(utxo["blockheight"].ToString());
                        if (height >= 0)
                        { //Do not count unconfirmed transactions
                            foreach (JToken token in utxo["tokens"])
                            {
                                //Like mentioned earlier, there can be more than one token per unspent
                                no_token = false;
                                string id = token["tokenId"].ToString();
                                if (id.Equals(tokenid) == true)
                                {
                                    //This is our desired token
                                    sat_amount += Decimal.Parse(token["amount"].ToString());
                                }
                            }
                            if (cointype == 0 && no_token == true)
                            {
                                //This is a pure Neblio unspent output, add to wallet value
                                sat_amount += Decimal.Parse(utxo["value"].ToString());
                            }
                        }
                        total_utxo++;
                        if (total_utxo > 1000) { break; } //For spam attack prevention
                    }

                    if (satoshi == true)
                    {
                        return sat_amount; //Will return a satoshi amount (very large) or token value
                    }
                    else
                    {
                        if (cointype == 0)
                        {
                            return Decimal.Divide(sat_amount, 100000000);
                        }
                        else
                        {
                            return sat_amount;
                        }
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to parse NTP1 data: " + e.ToString());
                }
                return 0;
            }

        }

		public static JArray GetAddressUnspentTX(DexConnection con, int cointype, string address)
        {
            //This will return a JArray that can be parsed with row data containing
            //Tx hash, Tx output number, Value and tokenID in case tokens exist
            int connectiontype = GetWalletBlockchainType(cointype);
            if (connectiontype != 0)
            {
                if (con == null)
                {
                    //Find a dexconnection

                    lock (DexConnectionList)
                    {
                        for (int i = 0; i < DexConnectionList.Count; i++)
                        {
                            if (DexConnectionList[i].contype == 1 && DexConnectionList[i].open == true && DexConnectionList[i].blockchain_type == connectiontype)
                            {
                                con = DexConnectionList[i];
                                break;
                            }
                        }
                    }

                    if (con == null) { return null; }
                }

                try
                {
                    string blockdata = "";
                    lock (con.blockhandle)
                    {
                        con.blockhandle.Reset();
                        string scripthash = GetElectrumScriptHash(address, cointype);
                        SendElectrumAction(con, 3, scripthash);
                        con.blockhandle.WaitOne(10000); //This will wait 10 seconds for a response
                        if (con.blockdata == "")
                        {
                            NebliDexNetLog("No electrum response");
                            //Disconnect from server so it doesn't happen again
                            con.open = false;
                            return null;
                        } //No response yet
                        blockdata = con.blockdata;
                    }

                    JObject js = JObject.Parse(blockdata);
                    if (js["result"] == null) { return null; }

                    JArray utxo = new JArray();

                    int total_utxo = 0;

                    //Go through each row of results
                    foreach (JToken row in js["result"])
                    {
                        int height = Convert.ToInt32(row["height"].ToString());
                        if (height > 0)
                        { //If the height is 0, the utxo is unconfirmed
                            JObject line = new JObject();
                            line["tx_hash"] = row["tx_hash"];
                            line["tx_pos"] = row["tx_pos"];
                            line["tx_value"] = row["value"];
                            utxo.Add(line);
                            total_utxo++;
                            if (total_utxo > 1000) { break; } //Only in cases of spam attacks
                        }
                    }

                    return utxo;
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to parse Electrum unspent output: " + e.ToString());
                }
                return null;
            }
            else
            {
                try
                {
                    string nodeurl = NEBLAPI_server; //Currently only one node at this time
                    bool timeout;
                    string response = "";
                    JObject result = null;
                    if (using_blockhelper == false)
                    {
                        if (using_cnblockhelper == false)
                        {
                            response = HttpRequest(nodeurl + "/ntp1/addressinfo/" + address, "", out timeout);
                            if (response.Length == 0) { return null; }
                            result = JObject.Parse(response);
                            if (result["code"] != null) { return null; } //This is also an error
                        }
                        else
                        {
                            //Querying the CN for UTXO information
                            JObject req = new JObject();
                            req["cn.method"] = "blockhelper.getaddressinfo";
                            req["cn.response"] = 0;
                            req["cn.address"] = address;
                            response = SendBlockHelperMessage(req, false);
                            if (response.Length == 0) { return null; }
                            result = JObject.Parse(response);
                            if (result["cn.result"] == null)
                            {
                                //This means error
                                NebliDexNetLog("Unable to acquire balance for neblio address " + address + ", error: " + result["cn.error"].ToString());
                                return null;
                            }
                            result["utxos"] = result["cn.result"];
                            result.Remove("cn.result");
                        }
                    }
                    else
                    {
                        //Querying our own blockhelper for information
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.getaddressinfo";
                        req["rpc.response"] = 0;
                        req["rpc.neb_address"] = address;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0) { return null; }
                        result = JObject.Parse(response);
                        if (result["rpc.result"] == null)
                        {
                            //This means error
                            NebliDexNetLog("Unable to acquire balance for neblio address " + address + ", error: " + result["rpc.error"].ToString());
                            return null;
                        }
                        result["utxos"] = result["rpc.result"];
                        result.Remove("rpc.result");
                    }

                    JArray utxo_array = new JArray();
                    int total_utxo = 0;

                    //Now parse the data and get the value  
                    foreach (JToken row in result["utxos"])
                    {
                        //Find the tokens in the unspent
                        int height = Convert.ToInt32(row["blockheight"].ToString());
                        if (height >= 0)
                        { //Do not look at unconfirmed tx
                            JObject line = new JObject();
                            line["tx_hash"] = row["txid"];
                            line["tx_pos"] = row["index"];
                            line["tx_value"] = row["value"];
                            line["tx_tokenid"] = "";
                            foreach (JToken token in row["tokens"])
                            {
                                line["tx_tokenid"] = token["tokenId"].ToString();
                                break; //Only get the first token ID, just to verify if tokens are there
                            }
                            utxo_array.Add(line);
                            total_utxo++;
                            if (total_utxo > 1000) { break; } //Spam attack cases
                        }
                    }

                    return utxo_array;
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to parse NTP1 data: " + e.ToString());
                }
                return null;
            }
        }

        public static bool running_consolidation_check = false;

        public static void WalletConsolidationCheck(object state)
        {
            //This function will periodically check each wallet and see if they are requesting consolidation
            //It will be ran every 6 hrs, including upon program opening
            bool too_soon = false;
            lock (MyOpenOrderList)
            {
                for (int i = 0; i < MyOpenOrderList.Count; i++)
                {
                    if (MyOpenOrderList[i].order_stage > 0) { too_soon = true; break; } //Your maker order is matching something
                    if (MyOpenOrderList[i].is_request == true) { too_soon = true; break; } //Already have another taker order
                }
            }
            if (too_soon == true) { return; } //Wait until trade is complete

            NebliDexNetLog("Wallet Consolidation Check...");
            running_consolidation_check = true;

            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].status == 0)
                {
					if (WalletList[i].blockchaintype == 6) { continue; } //Ethereum doesn't need to be consolidated
                    //Wallet must be available for use
                    JArray utxo = GetAddressUnspentTX(null, WalletList[i].type, WalletList[i].address);
                    if (utxo != null)
                    {
                        int total_utxo = 0;
                        bool mixed_unspent = false;
                        foreach (JToken row in utxo)
                        {
							if (WalletList[i].blockchaintype != 0)
                            {
                                total_utxo++;
                            }
                            else
                            {
                                //We need to account for the token type
                                string tokenid = row["tx_tokenid"].ToString();
                                if (tokenid.Equals(WalletList[i].TokenID) == true)
                                {
                                    total_utxo++;
                                    if (tokenid.Length > 0)
                                    {
                                        Decimal unspent_value = Decimal.Parse(row["tx_value"].ToString()); //Get the unspent value, if token, should be no more than 10000 sat
                                        if (unspent_value > 10000)
                                        {
                                            mixed_unspent = true; //Separate the token from the neblio
                                        }
                                    }
                                }
                            }
                        }

                        if (total_utxo > 20 || mixed_unspent == true)
                        {
                            if (MyOpenOrderList.Count > 0)
                            {
                                QueueAllOpenOrders();
                            }

                            //We have a whole bunch of unspent transactions, consolidate
                            decimal balance = WalletList[i].balance;
                            bool create_tx = true;

                            if (create_tx == true)
                            {
                                //Send the entire balance back to us
                                NebliDexNetLog("Consolidating balance for Wallet " + WalletList[i].Coin);
                                Transaction tx = App.CreateSignedP2PKHTx(WalletList[i].type, balance, WalletList[i].address, true, false);
                                //This should broadcast as well
                                if (tx != null)
                                {
                                    AddMyTxToDatabase(tx.GetHash().ToString(), WalletList[i].address, WalletList[i].address, balance, WalletList[i].type, 2, App.UTCTime());
                                }
                            }
                            //This should consolidate the account
                        }
                    }
                }
            }

            running_consolidation_check = false;
        }

		public static int TransactionConfirmations(int cointype, string txhash)
        {
            //We used to use the ref_address to get the confirmations amount, now we don't
            int connectiontype = GetWalletBlockchainType(cointype);
            if (connectiontype != 0)
            {
                if (connectiontype == 6)
                {
                    //This is an ethereum transaction
                    return EthereumTransactionConfirmations(txhash);
                }

                DexConnection con = null;

                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].contype == 1 && DexConnectionList[i].open == true && DexConnectionList[i].blockchain_type == connectiontype)
                        {
                            con = DexConnectionList[i];
                            break;
                        }
                    }
                }

                if (con == null) { return -1; }

                int confirmations = -1; //-1 confirmations means no connections
                try
                {
                    string blockdata = "";
                    lock (con.blockhandle)
                    { //Prevents other threads from doing blocking calls to connection
                        con.blockhandle.Reset();
                        SendElectrumAction(con, 4, txhash);
                        con.blockhandle.WaitOne(10000); //This will wait 10 seconds for a response
                        if (con.blockdata == "")
                        {
                            NebliDexNetLog("No electrum response");
                            //Disconnect from server so it doesn't happen again
                            con.open = false;
                            return -1;
                        } //No response yet
                        blockdata = con.blockdata;
                    }
                    NebliDexNetLog("Electrum TX Hash (" + txhash + ") Confirmation Info: " + blockdata);

                    JObject js = JObject.Parse(blockdata);

                    if (js["result"] == null)
                    {
                        if (js["error"] != null)
                        {
                            //Grab the code if available
                            JToken jcode = js["error"];
                            int code = Convert.ToInt32(jcode["code"].ToString());
                            if (code != 2)
                            {
                                //We expect code 2 for a while until transaction arrives to server mempool
                                //If we get any other error, drop the connection
                                //A whitelist instead of a blacklist
                                con.open = false;
                                return -1;
                            }
                        }
                        return 0;
                    } //No history
                    if (js["result"]["confirmations"] == null) { return 0; }

                    confirmations = Convert.ToInt32(js["result"]["confirmations"].ToString());


                }
                catch (Exception)
                {
                    NebliDexNetLog("Failed to find amount of transaction confirmations");
                }

                return confirmations;
            }
            else
            {
                try
                {
                    string nodeurl = NEBLAPI_server; //Currently only one node at this time
                    bool timeout;
                    string response = "";
                    JObject result = null;
                    if (using_blockhelper == false)
                    {
                        if (using_cnblockhelper == false)
                        {
                            response = HttpRequest(nodeurl + "/ntp1/transactioninfo/" + txhash, "", out timeout);
                            if (response.Length == 0) { return -1; }
                            result = JObject.Parse(response);
                            if (result["code"] != null) { return -1; } //This is also an error
                        }
                        else
                        {
                            //Querying the CN for Transaction info
                            JObject req = new JObject();
                            req["cn.method"] = "blockhelper.gettransactioninfo";
                            req["cn.response"] = 0;
                            req["cn.txhash"] = txhash;
                            response = SendBlockHelperMessage(req, false);
                            if (response.Length == 0) { return -1; }
                            result = JObject.Parse(response);
                            if (result["cn.result"] == null)
                            {
                                //This means transaction not found on blockchain (could be too early)
                                NebliDexNetLog("Unable to obtain the transaction hash " + txhash + ", error: " + result["cn.error"].ToString());
                                return 0;
                            }
                            result = (JObject)result["cn.result"];
                        }
                    }
                    else
                    {
                        //Querying our own blockhelper for information
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.gettransactioninfo";
                        req["rpc.response"] = 0;
                        req["rpc.txhash"] = txhash;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0) { return -1; }
                        result = JObject.Parse(response);
                        if (result["rpc.result"] == null)
                        {
                            //This means transaction not found on blockchain (could be too early)
                            NebliDexNetLog("Unable to obtain the transaction hash " + txhash + ", error: " + result["rpc.error"].ToString());
                            return 0;
                        }
                        result = (JObject)result["rpc.result"];
                    }

                    int confirmations = 0;
                    if (result["confirmations"] != null)
                    {
                        confirmations = Convert.ToInt32(result["confirmations"].ToString());
                    }

                    return confirmations;
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to parse NTP1 data: " + e.ToString());
                }
                return -1;
            }
        }
      
        //Transaction to Database easy functions
        public static void AddValidatingTransaction(JObject databasejs)
        {

            //This will add a database entry to the validating transactions database
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //For Atomic Swap functions, we use: utctime, order_nonce_ref, maker_feetx (posted with taker txs after taker acks information)
            //redeemscript_add (the contract address of taker transaction), validating_cn_pubkey, market (trade market), reqtype (type of order request)
            //taker_feetx (posted with taker tx and maker feetx), taker_tx* (we store taker tx and wait till maker and taker ack process)
            //Validator will only monitor taker contract and wait for it to empty before closing connection to trader nodes and reposting order

            //Add certain values to the database
            string myquery = "Insert Into VALIDATING_TRANSACTIONS (utctime, order_nonce_ref, redeemscript_add, validating_cn_pubkey, market, reqtype,";
            myquery += " taker_feetx, taker_tx, status, claimed, waittime)";
            myquery += " Values (@time, @nonce, @redeem, @cnpubkey, @mark, @type, @tfeetx, @ttx, 0, 0, @wait);";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", Convert.ToInt32(databasejs["utctime"].ToString()));
            statement.Parameters.AddWithValue("@nonce", databasejs["nonce"].ToString());
            statement.Parameters.AddWithValue("@redeem", databasejs["redeemscript_add"].ToString());
            statement.Parameters.AddWithValue("@cnpubkey", databasejs["cn_pubkey"].ToString());
            statement.Parameters.AddWithValue("@mark", databasejs["market"].ToString());
            statement.Parameters.AddWithValue("@type", databasejs["type"].ToString());
            statement.Parameters.AddWithValue("@tfeetx", databasejs["taker_feetx"].ToString());
            statement.Parameters.AddWithValue("@ttx", databasejs["taker_tx"].ToString());
            statement.Parameters.AddWithValue("@wait", UTCTime());
            statement.ExecuteNonQuery();
            statement.Dispose();

            mycon.Close();
        }
      
        public static string GetValidationData(string field, int time, string order_nonce)
        {
            //This will return data from the field matching the time and order_nonce
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select all the rows from tradehistory
            string myquery = "Select " + field + " From VALIDATING_TRANSACTIONS Where utctime = @time And order_nonce_ref = @nonce";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", time);
            statement.Parameters.AddWithValue("@nonce", order_nonce);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            bool dataavail = statement_reader.Read();
            string data = "";
            if (dataavail == true)
            {
                data = statement_reader[field].ToString();
            }
            statement_reader.Close();
            statement.Dispose();
            mycon.Close();
            return data;
        }

        public static bool SetValidationData(string field, string val, int time, string order_nonce)
        {
            //This will return data from the field matching the time and order_nonce
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select all the rows from tradehistory
            string myquery = "Update VALIDATING_TRANSACTIONS Set " + field + " = @val Where utctime = @time And order_nonce_ref = @nonce";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@val", val);
            statement.Parameters.AddWithValue("@time", time);
            statement.Parameters.AddWithValue("@nonce", order_nonce);
            int rows = statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
            if (rows > 0) { return true; } //If there was something to update
            NebliDexNetLog("Unable to save :" + field + " value: " + val + " to database");
            return false;
        }

        public static bool SetValidationData(string field, int val, int time, string order_nonce)
        {
            //This will return data from the field matching the time and order_nonce
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select all the rows from tradehistory
            string myquery = "Update VALIDATING_TRANSACTIONS Set " + field + " = @val Where utctime = @time And order_nonce_ref = @nonce";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@val", val);
            statement.Parameters.AddWithValue("@time", time);
            statement.Parameters.AddWithValue("@nonce", order_nonce);
            int rows = statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();
            if (rows > 0) { return true; } //If there was something to update
            NebliDexNetLog("Unable to save :" + field + " value: " + val + " to database");
            return false;
        }

        public static void CheckChartSync(object state)
        {
            //This method currently not used, save for future use

            if (critical_node == false) { return; } //A CN function only
                                                    //Run this function only once after 95 minutes of being a critical node
            NebliDexNetLog("Checking charts to make sure they are in sync");

            //This function will check to see if the charts data is too spaced out, if it is, it will try to obtain a sync with another node
            int max_chart24hgap = 60 * 16; //If the point gap is greater than 16 minutes then resync
            int max_chart7dgap = 60 * 91; //If the point gap is greater than 91 minutes then resync

            string myquery;
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            for (int market = 0; market < total_markets; market++)
            {
                int backtime = App.UTCTime() - 60 * 60 * 25;
                myquery = "Select utctime From CANDLESTICKS24H Where market = @mark And utctime > @time Order By utctime ASC"; //Show results from oldest to most recent
                statement = new SqliteCommand(myquery, mycon);
                statement.Parameters.AddWithValue("@time", backtime);
                statement.Parameters.AddWithValue("@mark", market);
                SqliteDataReader statement_reader = statement.ExecuteReader();
                int oldutc = -1;
                while (statement_reader.Read())
                {
                    if (oldutc == -1)
                    {
                        oldutc = Convert.ToInt32(statement_reader["utctime"].ToString());
                    }
                    else
                    {
                        int ctime = Convert.ToInt32(statement_reader["utctime"].ToString());
                        if (ctime - oldutc > max_chart24hgap)
                        {
                            NebliDexNetLog("24 hour chart is out of sync, resyncing now");
                            //We need to resync the charts
                            reconnect_cn = true;
                            cn_network_down = true; //Force the CN to redownload the chart
                            break;
                        }
                        oldutc = ctime;
                    }
                }
                statement_reader.Close();
                statement.Dispose();

                //Do same for 7 day
                backtime = App.UTCTime() - (int)Math.Round(60.0 * 60.0 * 24.0 * 6.25);
                myquery = "Select utctime From CANDLESTICKS7D Where market = @mark And utctime > @time Order By utctime ASC"; //Show results from oldest to most recent
                statement = new SqliteCommand(myquery, mycon);
                statement.Parameters.AddWithValue("@time", backtime);
                statement.Parameters.AddWithValue("@mark", market);
                statement_reader = statement.ExecuteReader();
                oldutc = -1;
                while (statement_reader.Read())
                {
                    if (oldutc == -1)
                    {
                        oldutc = Convert.ToInt32(statement_reader["utctime"].ToString());
                    }
                    else
                    {
                        int ctime = Convert.ToInt32(statement_reader["utctime"].ToString());
                        if (ctime - oldutc > max_chart7dgap)
                        {
                            NebliDexNetLog("7 day chart is out of sync, resyncing now");
                            //We need to resync the charts
                            reconnect_cn = true;
                            cn_network_down = true; //Force the CN to redownload the chart
                            break;
                        }
                        oldutc = ctime;
                    }
                }
                statement_reader.Close();
                statement.Dispose();
            }
            mycon.Close();
        }

        public static void PruneDatabases()
        {
            //This function is a simple function that removes old data from the files
            string myquery;
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Delete the Candles Database if they have more than 30 days worth of data
            myquery = "Delete From CANDLESTICKS7D Where utctime < @time";
            int backtime = UTCTime() - 60 * 60 * 24 * 30; //Long ago in the past
            SqliteCommand statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", backtime);
            statement.ExecuteNonQuery();
            statement.Dispose();

            myquery = "Delete From CANDLESTICKS24H Where utctime < @time";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", backtime);
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Remove blacklisted IPs after 10 days
            myquery = "Delete From BLACKLIST Where utctime < @time";
            backtime = UTCTime() - 60 * 60 * 24 * 10; //Long ago in the past
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", backtime);
            statement.ExecuteNonQuery();
            statement.Dispose();

            mycon.Close();

            //Remove the debug log if it gets too large
            if (File.Exists(App_Path + "/data/debug.log") == true)
            {
                long filelength = new System.IO.FileInfo(App_Path + "/data/debug.log").Length;
                if (filelength > 10000000)
                { //Debug log is greater than 10MB
                    lock (debugfileLock)
                    {
                        File.Delete(App_Path + "/data/debug.log");
                    }
                }
            }

        }

        public static void ExportTradeHistory(string fname)
        {
            try
            {
                using (System.IO.StreamWriter file_out =
                new System.IO.StreamWriter(@fname, false))
                {
                    file_out.WriteLine("Date,Market,Price,TradeAmount,BaseAmount");

                    //Load the trade history
                    SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
                    mycon.Open();

                    //Set our busy timeout, so we wait if there are locks present
                    SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
                    statement.ExecuteNonQuery();
                    statement.Dispose();

                    //Select all the rows from tradehistory
                    string myquery = "Select utctime, market, type, price, amount From MYTRADEHISTORY Where pending = 0 Order By utctime ASC";
                    statement = new SqliteCommand(myquery, mycon);
                    SqliteDataReader statement_reader = statement.ExecuteReader();
                    while (statement_reader.Read())
                    {
                        int utctime = Convert.ToInt32(statement_reader["utctime"]);
                        int market = Convert.ToInt32(statement_reader["market"]);
                        string format_date = UTC2DateTime(utctime).ToString("yyyy-MM-dd");
                        string format_market = App.MarketList[market].format_market;
                        decimal price = Convert.ToDecimal(statement_reader["price"].ToString(), CultureInfo.InvariantCulture);
                        decimal amount = Convert.ToDecimal(statement_reader["amount"].ToString(), CultureInfo.InvariantCulture);
                        string format_price = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", price);
                        string format_amount = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount);
                        string format_baseamount = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount * price);
                        int type = Convert.ToInt32(statement_reader["type"]);
                        if (type == 0)
                        {
                            file_out.WriteLine(format_date + "," + format_market + "," + format_price + "," + format_amount + ",-" + format_baseamount);
                        }
                        else
                        {
                            file_out.WriteLine(format_date + "," + format_market + "," + format_price + ",-" + format_amount + "," + format_baseamount);
                        }
                    }
                    statement_reader.Close();
                    statement.Dispose();
                    mycon.Close();
                }
            }
            catch (Exception)
            {
                NebliDexNetLog("Failed to save trade history");
            }
        }

        public static void ExportCNFeeHistory(string fname)
        {
            try
            {
                using (System.IO.StreamWriter file_out =
                new System.IO.StreamWriter(@fname, false))
                {
                    file_out.WriteLine("Date,Market,FeeCollected");

                    //Load the trade history
                    SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
                    mycon.Open();

                    //Set our busy timeout, so we wait if there are locks present
                    SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
                    statement.ExecuteNonQuery();
                    statement.Dispose();

                    //Select all the rows from cn fees
                    string myquery = "Select utctime, market, fee From CNFEES Order By utctime ASC";
                    statement = new SqliteCommand(myquery, mycon);
                    SqliteDataReader statement_reader = statement.ExecuteReader();
                    while (statement_reader.Read())
                    {
                        int utctime = Convert.ToInt32(statement_reader["utctime"]);
                        int market = Convert.ToInt32(statement_reader["market"]);
                        string format_date = UTC2DateTime(utctime).ToString("yyyy-MM-dd");
                        string format_market = App.MarketList[market].format_market;
                        decimal fee = Convert.ToDecimal(statement_reader["fee"].ToString(), CultureInfo.InvariantCulture);
                        string format_fee = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", fee);
                        file_out.WriteLine(format_date + "," + format_market + "," + format_fee);
                    }
                    statement_reader.Close();
                    statement.Dispose();
                    mycon.Close();
                }
            }
            catch (Exception)
            {
                NebliDexNetLog("Failed to save cn fee history");
            }
        }

        public static Transaction GenerateScratchNTP1Transaction(string from_add, JArray targets, ref Decimal input_total)
        {
            //This is a new function that will create a new transaction with all the non-neblio inputs and token outputs including op_return
            //It is formatted in the ideal format for NebliDex (borrowed from BlockHelper code)

            //First get how many token types we are sending in this transaction
            List<string> token_types = new List<string>();
            foreach (JObject target in targets)
            {
                string unique_id = target["tokenId"].ToString();
                bool exist = false;
                for (int i = 0; i < token_types.Count; i++)
                {
                    if (unique_id == token_types[i])
                    {
                        exist = true; break; //We already have this token type in our list
                    }
                }
                if (exist == false)
                {
                    //Add this new token type to our list
                    if (unique_id.Trim().Length == 0) { return null; } //No tokenID
                    token_types.Add(unique_id);
                }
            }

            if (token_types.Count < 1) { return null; } //Nothing to send

            //Next we obtain a list of all the UTXOs for this address
            string response = "";
            JObject result = null;
            if (using_blockhelper == false)
            {
                if (using_cnblockhelper == false)
                {
					string nodeurl = NEBLAPI_server;
                    bool timeout;
                    response = HttpRequest(nodeurl + "/ntp1/addressinfo/" + from_add, "", out timeout);
                    if (response.Length == 0) { return null; }
                    result = JObject.Parse(response);
                    if (result["code"] != null) { return null; } //This is also an error
                }
                else
                {
                    //Querying the CN for UTXO information
                    JObject req = new JObject();
                    req["cn.method"] = "blockhelper.getaddressinfo";
                    req["cn.response"] = 0;
                    req["cn.address"] = from_add;
                    response = SendBlockHelperMessage(req, false);
                    if (response.Length == 0) { return null; }
                    result = JObject.Parse(response);
                    if (result["cn.result"] == null)
                    {
                        //This means error
                        NebliDexNetLog("Unable to acquire balance for neblio address " + from_add + ", error: " + result["cn.error"].ToString());
                        return null;
                    }
                    result["utxos"] = result["cn.result"];
                    result.Remove("cn.result");
                }
            }
            else
            {
                //Querying our own blockhelper for information
                JObject req = new JObject();
                req["rpc.method"] = "rpc.getaddressinfo";
                req["rpc.response"] = 0;
                req["rpc.neb_address"] = from_add;
                response = SendBlockHelperMessage(req, true);
                if (response.Length == 0) { return null; }
                result = JObject.Parse(response);
                if (result["rpc.result"] == null)
                {
                    //This means error
                    NebliDexNetLog("Unable to acquire balance for neblio address " + from_add + ", error: " + result["rpc.error"].ToString());
                    return null;
                }
                result["utxos"] = result["rpc.result"];
                result.Remove("rpc.result");
            }
            JArray unordered_utxos = (JArray)result["utxos"];
            if (unordered_utxos.Count == 0) { return null; } //Should not happen

            Dictionary<string, long> tokeninput_amounts = new Dictionary<string, long>();
            Dictionary<string, long> tokenoutput_amounts = new Dictionary<string, long>();

            JArray ordered_utxos = new JArray();
            JArray ordered_targets = new JArray();
            for (int i = 0; i < token_types.Count; i++)
            {
                bool match = false;
                foreach (JObject utxo in unordered_utxos)
                {
					bool token_present = false;
                    int token_count = 0;
                    foreach (JObject token in utxo["tokens"])
                    {
                        //Go through the list of tokens in this UTXO, may have duplicate tokens
                        if (token["tokenId"].ToString() == token_types[i])
                        {
                            token_present = true;
                            match = true;
                            if (tokeninput_amounts.ContainsKey(token_types[i]) == false)
                            {
                                tokeninput_amounts[token_types[i]] = Convert.ToInt64(token["amount"].ToString());
                            }
                            else
                            {
                                tokeninput_amounts[token_types[i]] += Convert.ToInt64(token["amount"].ToString()); //Get the amount of this token type
                            }
                        }
                        token_count++;
                    }
                    if (token_present == true)
                    {
                        if (token_count > 1)
                        {
                            // More than 1 token at this UTXO, check whether they are mixed tokens (shouldn't happen normally)
                            string firstToken = "";
                            foreach (JObject token in utxo["tokens"])
                            {
                                if (firstToken.Length == 0)
                                {
                                    firstToken = token["tokenId"].ToString();
                                }
                                else
                                {
                                    if (firstToken.Equals(token["tokenId"].ToString()) == false)
                                    {
                                        NebliDexNetLog("UTXO contains multiple different tokens, not allowed in current configuration");
                                        return null;
                                    }
                                }
                            }
                        }
                        ordered_utxos.Add(utxo); // Our desired token is in UTXO, add the UTXO in order
                    }
                }
                if (match == false)
                {
                    return null; //This means for this token type, it couldn't find a corresponding UTXO
                }
                foreach (JObject target in targets)
                {
                    if (target["tokenId"].ToString() == token_types[i])
                    {
                        if (tokenoutput_amounts.ContainsKey(token_types[i]) == false)
                        {
                            tokenoutput_amounts[token_types[i]] = Convert.ToInt64(target["amount"].ToString());
                        }
                        else
                        {
                            tokenoutput_amounts[token_types[i]] += Convert.ToInt64(target["amount"].ToString()); //Get the amount of this token type
                        }
                        ordered_targets.Add(target);
                    }
                }
                //By the end of this, we should have an ordered list of utxos based on order of token types sorted by first tokenid
                //We should also have an ordered list of targets sorted by tokenid
            }

            if (ordered_utxos.Count < 1) { return null; } //There are no matching UTXOs for the token type

            for (int i = 0; i < token_types.Count; i++)
            {
                long outs = tokenoutput_amounts[token_types[i]];
                long ins = tokeninput_amounts[token_types[i]];
                if (tokenoutput_amounts[token_types[i]] != tokeninput_amounts[token_types[i]])
                {
                    //We are not spending the right amount of token inputs. We must spend the entire input
                    NebliDexNetLog("Token spent must equal token input. Entire balance must be spent");
                    return null;
                }
            }

            //Create the Neblio based transaction
            Transaction tx = new Transaction();
            tx.hasTimeStamp = true;

            //Add the inputs
            foreach (JObject utxo in ordered_utxos)
            {
                Decimal val = Convert.ToDecimal(utxo["value"].ToString());
                input_total = Decimal.Add(input_total, val); //Add to the satoshi amount
                OutPoint utxout = OutPoint.Parse(utxo["txid"].ToString() + "-" + utxo["index"].ToString()); //Create the Outpoint
                TxIn tx_in = new TxIn();
                tx_in.PrevOut = utxout;
                tx.Inputs.Add(tx_in);
            }

            //Now create the ordered outputs
            List<NTP1Instructions> TiList = new List<NTP1Instructions>();
            foreach (JObject target in ordered_targets)
            {
                string target_address = target["address"].ToString();
                Script out_script = GetAddressScriptPubKey(target_address, 0); //Neblio based wallet
                TxOut target_out = new TxOut()
                {
                    Value = new Money(10000), //0.0001 Neblio
                    ScriptPubKey = out_script
                };
                tx.Outputs.Add(target_out);

                //Now make the transfer instruction
                NTP1Instructions ti = new NTP1Instructions();
                ti.amount = Convert.ToUInt64(target["amount"].ToString());
                ti.vout_num = tx.Outputs.Count - 1;
                TiList.Add(ti);
            }

            //Create the hex op_return
            string ti_script = _NTP1CreateTransferScript(TiList, null); //No metadata

            //Now add the op_return
            Script nulldata_script = new Script("OP_RETURN " + ti_script);
            TxOut tx_out = new TxOut()
            {
                Value = new Money(10000), //0.0001 Neblio
                ScriptPubKey = nulldata_script
            };
            tx.Outputs.Add(tx_out);

            return tx;
        }

        public static string _NTP1CreateTransferScript(List<NTP1Instructions> TIs, byte[] metadata)
        {
            if (TIs.Count == 0) { return ""; }
            if (TIs.Count > 255) { return ""; } //Cannot create transaction greater than 255 instructions

            //Constants
            byte[] header = ConvertHexStringToByteArray("4e5403"); //Represents chars NT and byte protocal version (3)
            byte op_code = 16; //Transer transaction
            int op_return_max_size = 4096; //Maximum size of the scriptbin

            using (MemoryStream scriptbin = new MemoryStream())
            {
                //This stream will hold the byte array that will be converted to a hex string eventually
                scriptbin.Write(header, 0, header.Length); //Write the header data to the stream
                scriptbin.WriteByte(op_code);
                scriptbin.WriteByte(Convert.ToByte(TIs.Count)); //The amount of TIs

                for (int i = 0; i < TIs.Count; i++)
                {
                    //Add the transfer instructions
                    NTP1Instructions ti = TIs[i];
                    //Skip input will always be false in our case, so first position is always 0
                    //The maximum vout position is 31 (0-31) per this method, although 255 TIs are supported
                    if (ti.vout_num > 31) { return ""; }
                    string output_binary = "000" + Convert.ToString(ti.vout_num, 2).PadLeft(5, '0'); //Convert number to binary
                    byte first_byte = Convert.ToByte(output_binary, 2);
                    scriptbin.WriteByte(first_byte);

                    //Now convert the amount to byte array
                    byte[] amount_bytes = _NTP1NumToByteArray(ti.amount);
                    scriptbin.Write(amount_bytes, 0, amount_bytes.Length);
                }

                byte[] all = scriptbin.ToArray();

                //Add metadata if present
                if (metadata != null)
                {
                    uint msize = (uint)metadata.Length;
                    if (msize > 0)
                    {
                        byte[] msize_bytes = BitConverter.GetBytes(msize);
                        if (BitConverter.IsLittleEndian == true)
                        {
                            //We must convert this to big endian as protocol requires big endian
                            Array.Reverse(msize_bytes);
                        }
                        scriptbin.Write(msize_bytes, 0, msize_bytes.Length); //Write the size of the metadata
                        scriptbin.Write(metadata, 0, metadata.Length); //Write the length of the metadata
                    }
                }
                if (scriptbin.Length > op_return_max_size)
                {
                    return ""; //Cannot create a script larger than the max
                }
                return ConvertByteArrayToHexString(scriptbin.ToArray());
            }
        }

        public static byte[] ConvertHexStringToByteArray(string hexString)
        {
            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException(String.Format(CultureInfo.InvariantCulture, "The binary key cannot have an odd number of digits: {0}", hexString));
            }

            byte[] data = new byte[hexString.Length / 2];
            for (int index = 0; index < data.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                data[index] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return data;
        }

        public static string ConvertByteArrayToHexString(byte[] arr)
        {
            string[] result = new string[arr.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                result[i] = arr[i].ToString("X2").ToLower(); //Hex format
            }
            return String.Concat(result); //Returns all the strings together    
        }

        public static byte[] _NTP1NumToByteArray(ulong amount)
        {
            //We need to calculate the Mantissa and Exponent for this number
            if (amount == 0) { return null; }
            string amount_string = amount.ToString();
            //Get the amount of significant digits
            int zerocount = 0;
            for (int i = amount_string.Length - 1; i >= 0; i--)
            {
                if (amount_string[i] == '0')
                {
                    zerocount++;
                }
                else
                {
                    break;
                }
            }

            int significant_digits = amount_string.Length - zerocount;
            if (amount < 32 || significant_digits > 12)
            {
                zerocount = 0; //If the significant digits are too large or amount is small, do not utilize exponent
                significant_digits = amount_string.Length;
            }

            long mantissa = Convert.ToInt64(amount_string.Substring(0, significant_digits));
            string mantissa_binary = Convert.ToString(mantissa, 2);
            string exponent_binary = Convert.ToString(zerocount, 2);
            if (zerocount == 0)
            {
                //No exponent
                exponent_binary = "";
            }

            //No need to remove the leading zeros as Neblio does due to the fact that C# creates binary string without leading zeros
            int mantissaSize = 0;
            int exponentSize = 0;
            string header;

            if (mantissa_binary.Length <= 5 && exponent_binary.Length == 0)
            {
                header = "000";
                mantissaSize = 5;
                exponentSize = 0;
            }
            else if (mantissa_binary.Length <= 9 && exponent_binary.Length <= 4)
            {
                header = "001";
                mantissaSize = 9;
                exponentSize = 4;
            }
            else if (mantissa_binary.Length <= 17 && exponent_binary.Length <= 4)
            {
                header = "010";
                mantissaSize = 17;
                exponentSize = 4;
            }
            else if (mantissa_binary.Length <= 25 && exponent_binary.Length <= 4)
            {
                header = "011";
                mantissaSize = 25;
                exponentSize = 4;
            }
            else if (mantissa_binary.Length <= 34 && exponent_binary.Length <= 3)
            {
                header = "100";
                mantissaSize = 34;
                exponentSize = 3;
            }
            else if (mantissa_binary.Length <= 42 && exponent_binary.Length <= 3)
            {
                header = "101";
                mantissaSize = 42;
                exponentSize = 3;
            }
            else if (mantissa_binary.Length <= 54 && exponent_binary.Length == 0)
            {
                header = "11";
                mantissaSize = 54;
                exponentSize = 0;
            }
            else
            {
                //Can't encode binary format
                return null;
            }

            //Pad the mantissa and exponent binary format
            mantissa_binary = mantissa_binary.PadLeft(mantissaSize, '0');
            exponent_binary = exponent_binary.PadLeft(exponentSize, '0');

            string combined_binary = header + mantissa_binary + exponent_binary;
            if (combined_binary.Length % 8 != 0)
            {
                //Not divisible by 8, binary format incorrect
                return null;
            }

            byte[] encodedamount = new byte[combined_binary.Length / 8];
            for (int i = 0; i < combined_binary.Length; i += 8)
            {
                //Go through the binary string and find the bytes and put it in encodedamount
                byte b = Convert.ToByte(combined_binary.Substring(i, 8), 2);
                encodedamount[i / 8] = b;
            }

            return encodedamount;
        }

        public static List<NTP1Instructions> _NTP1ParseScript(string ntp1_opreturn)
        {
            if (ntp1_opreturn.Length == 0) { return null; } //Nothing inside
            byte[] scriptbin = ConvertHexStringToByteArray(ntp1_opreturn);
            int ntp1_protocol_version = Convert.ToInt32(scriptbin[2]);
            if (ntp1_protocol_version != 3)
            {
                throw new Exception("NTP1 Protocol Version less than 3 not currently supported");
            }
            int op_code = Convert.ToInt32(scriptbin[3]);
            int script_type = 0;
            if (op_code == 1)
            {
                script_type = 0; //Issue transaction
            }
            else if (op_code == 16)
            {
                script_type = 1; //Transfer transaction
            }
            else if (op_code == 32)
            {
                script_type = 2; //Burn transaction
            }
            if (script_type != 1)
            {
                throw new Exception("Can only parse NTP1 transfer scripts at this time");
            }
            scriptbin = _ByteArrayErase(scriptbin, 4); //Erase the first 4 bytes

            //Now obtain the size of the transfer instructions
            int numTI = Convert.ToInt32(scriptbin[0]); //Byte number between 0 and 255 inclusively
            int raw_size = 1;
            scriptbin = _ByteArrayErase(scriptbin, 1);
            List<NTP1Instructions> ti_list = new List<NTP1Instructions>();
            for (int i = 0; i < numTI; i++)
            {
                NTP1Instructions ti = new NTP1Instructions();
                ti.firstbyte = scriptbin[0]; //This represents the flags
                int size = _CalculateAmountSize(scriptbin[1]); //This will indicate the size of the next byte sequence
                byte[] amount_byte = new byte[size];
                Array.Copy(scriptbin, 1, amount_byte, 0, size); //Put these bytes into the amount_byte array
                size++; //Now include the flag byte
                raw_size += size; //Our total instruction size added to the raw size
                scriptbin = _ByteArrayErase(scriptbin, size); //Erase transfer instructions

                //Break down the first byte which represents the location and skipinput byte
                string firstbyte_bin = Convert.ToString(ti.firstbyte, 2).PadLeft(8, '0'); //Byte to binary string
                string outputindex_bin = firstbyte_bin.Substring(3);
                if (firstbyte_bin[7] == '1')
                {
                    ti.skipinput = true; //This means the byte value is 255
                }
                ti.vout_num = Convert.ToInt32(Convert.ToUInt64(outputindex_bin, 2));
                int len = amount_byte.Length;
                ti.amount = _NTP1ByteArrayToNum(amount_byte);
                ti_list.Add(ti);
            }

            return ti_list; //We do not care about the metadata

        }

        public static int _CalculateAmountSize(byte val)
        {
            string binval = Convert.ToString(val, 2).PadLeft(8, '0'); //Byte to binary string
            binval = binval.Substring(0, 3);
            ulong newval = Convert.ToUInt64(binval, 2);
            if (newval < 6)
            {
                return Convert.ToInt32(newval) + 1;
            }
            else
            {
                return 7;
            }
        }

        public static byte[] _ByteArrayErase(byte[] arr, long length)
        {
            //This will effectively delete a part of the byte array
            byte[] new_arr = new byte[arr.Length - length];
            Array.Copy(arr, length, new_arr, 0, new_arr.Length);
            return new_arr;
        }

        public static ulong _NTP1ByteArrayToNum(byte[] byteval)
        {
            if (byteval.Length > 7)
            {
                throw new Exception("Too many bytes to read. Cannot process");
            }
            int length = byteval.Length;
            string bin_set = "";
            for (int i = 0; i < length; i++)
            {
                //Create a new binary set that represents this entire byte sequence but byte sequence in reverse order and bit sequence in reverse order
                string binary_val = Convert.ToString(byteval[length - i - 1], 2).PadLeft(8, '0'); //Get the binary representation of this char value
                binary_val = _ReverseString(binary_val); //Reverse the binary number
                bin_set += binary_val;
            }

            int bit0 = Convert.ToInt32(char.GetNumericValue(bin_set[bin_set.Length - 1]));
            int bit1 = Convert.ToInt32(char.GetNumericValue(bin_set[bin_set.Length - 2]));
            int bit2 = Convert.ToInt32(char.GetNumericValue(bin_set[bin_set.Length - 3]));

            // sizes in bits            
            int headerSize = 0;
            int mantissaSize = 0;
            int exponentSize = 0;

            if (bit0 > 0 && bit1 > 0)
            {
                headerSize = 2;
                mantissaSize = 54;
                exponentSize = 0;
            }
            else
            {
                headerSize = 3;
                if (bit0 == 0 && bit1 == 0 && bit2 == 0)
                {
                    mantissaSize = 5;
                    exponentSize = 0;
                }
                else if (bit0 == 0 && bit1 == 0 && bit2 == 1)
                {
                    mantissaSize = 9;
                    exponentSize = 4;
                }
                else if (bit0 == 0 && bit1 == 1 && bit2 == 0)
                {
                    mantissaSize = 17;
                    exponentSize = 4;
                }
                else if (bit0 == 0 && bit1 == 1 && bit2 == 1)
                {
                    mantissaSize = 25;
                    exponentSize = 4;
                }
                else if (bit0 == 1 && bit1 == 0 && bit2 == 0)
                {
                    mantissaSize = 34;
                    exponentSize = 3;
                }
                else if (bit0 == 1 && bit1 == 0 && bit2 == 1)
                {
                    mantissaSize = 42;
                    exponentSize = 3;
                }
                else
                {
                    throw new Exception("Binary format not accepted");
                }
            }

            // ensure that the total size makes sense           
            int totalBitSize = headerSize + mantissaSize + exponentSize;
            if ((totalBitSize / 8) != byteval.Length || (totalBitSize % 8) != 0)
            {
                throw new Exception("Value of binary digits not valid");
            }

            //Now reverse the bin_set back to original form before reading the mantissa and the exponent
            bin_set = _ReverseString(bin_set);
            string mantissa = bin_set.Substring(headerSize, mantissaSize);
            string exponent = bin_set.Substring(headerSize + mantissaSize, exponentSize);
            if (exponent.Length == 0)
            {
                //Zero exponent
                exponent = "0000";
            }

            ulong mantissa_val = Convert.ToUInt64(mantissa, 2);
            ulong exponent_val = Convert.ToUInt64(exponent, 2);

            ulong amount = Convert.ToUInt64(Convert.ToDecimal(mantissa_val) * Convert.ToDecimal(Math.Pow(10, exponent_val)));
            return amount;
        }

        public static string _ReverseString(string s)
        {
            char[] arr = s.ToCharArray();
            Array.Reverse(arr);
            return new string(arr);
        }
        
        public static bool ByteArrayCompare(byte[] a1, byte[] a2)
        {
            if (a1.Length != a2.Length) { return false; }
            for (int i = 0; i < a1.Length; i++)
            {
                if (a1[i] != a2[i])
                {
                    return false;
                }
            }
            return true;
        }

		public static long GetBlockInclusionTime(int cointype, long unlock_time)
        {
            //Litecoin and Bitcoin utilize BIP113 which doesn't allow transactions to enter blockchain if
            //transaction locktime is greater than median time of last 11 blocks. They do not use the current time.
            //This method returns the expected current time the transaction would be included based on a locktime
            uint blockchain_seconds = 30; //Neblio is 30 second blocktimes
            int blockchain_type = GetWalletBlockchainType(cointype);
            if (blockchain_type == 1 || blockchain_type == 4)
            {
                //Bitcoin and Bitcoin Cash
                blockchain_seconds = 600; //10 Minutes
            }
            else if (blockchain_type == 2)
            {
                //Litecoin
                blockchain_seconds = 150; //2.5 minutes
            }
            else if (blockchain_type == 3)
            {
                //Groestlcoin
                blockchain_seconds = 60; //1 minute
            }
            else if (blockchain_type == 5)
            {
                //Monacoin
                blockchain_seconds = 90; //1.5 minutes
            }
            else if (blockchain_type == 6)
            {
                //Ethereum
                blockchain_seconds = 15; //15 seconds
            }
            uint wait_time = blockchain_seconds * 6;
            return unlock_time + wait_time;
        }

        public static string ExtractAtomicSwapSecretFromASM(string asm, byte[] secrethash)
        {
            string[] parts = asm.Split(' '); //Divide the string into parts
            for (int i2 = 0; i2 < parts.Length; i2++)
            {
                //Go through all of the pushdata
                if (parts[i2].IndexOf("[", StringComparison.InvariantCulture) > -1 || parts[i2].IndexOf("_", StringComparison.InvariantCulture) > -1)
                {
                    continue; //Skip parts that have non-hex signature information or op code
                }
                if (parts[i2].Length % 2 != 0)
                {
                    //Not a multiple of 2
                    continue;
                }
                byte[] partbytes = ConvertHexStringToByteArray(parts[i2]);
                if (partbytes.Length != 33)
                {
                    //Secret should be 33 bytes
                    continue;
                }
                //Finally SHA256 hash this byte array
                byte[] hex_bytes = NBitcoin.Crypto.Hashes.SHA256(partbytes);
                if (ByteArrayCompare(hex_bytes, secrethash) == true)
                {
                    //This is a match. We found our secret!
                    return parts[i2]; //This is the secret
                }
            }
            return "";
        }

		public static string GetAtomicSwapSecret(int cointype, string address, string secrethash_hex)
        {
            //This method will search the blockchain and find a hex array value that will hash sha256 to secrethash
            byte[] secrethash = ConvertHexStringToByteArray(secrethash_hex);
            int connectiontype = GetWalletBlockchainType(cointype);
            if (connectiontype != 0)
            {
                if (connectiontype == 6)
                {
                    //Ethereum has its own method to grab the secret
					return GetEthereumAtomicSwapSecret(secrethash_hex, Wallet.CoinERC20(cointype));
                }

                DexConnection dex = null;

                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].contype == 1 && DexConnectionList[i].open == true && DexConnectionList[i].blockchain_type == connectiontype)
                        {
                            dex = DexConnectionList[i];
                            break;
                        }
                    }
                }

                if (dex == null) { return ""; }

                try
                {
                    string blockdata = "";
                    lock (dex.blockhandle)
                    { //Prevents other threads from doing blocking calls to connection
                        dex.blockhandle.Reset();
                        string scripthash = GetElectrumScriptHash(address, cointype);
                        SendElectrumAction(dex, 7, scripthash); //Get the history of the address
                        dex.blockhandle.WaitOne(10000); //This will wait 10 seconds for a response
                        if (dex.blockdata == "")
                        {
                            NebliDexNetLog("No electrum response");
                            //Disconnect from server so it doesn't happen again
                            dex.open = false;
                            return "";
                        } //No response
                        blockdata = dex.blockdata;
                    }

                    JObject result = JObject.Parse(blockdata);
                    if (result["result"] == null)
                    {
                        NebliDexNetLog("Unable to obtain address information: " + address);
                        return "";
                    }

                    //Now parse the history data into an array
                    //History is sorted from oldest to newest
                    JArray result_array = (JArray)result["result"];
                    if (result_array.Count < 2)
                    {
                        //Only one transaction, there is no out transaction yet
                        NebliDexNetLog("No spending transaction yet at address: " + address);
                        return "";
                    }

                    //Otherwise look at all the txids from last to first
                    for (int txit = result_array.Count - 1; txit >= 0; txit--)
                    {
                        string txid = result_array[txit]["tx_hash"].ToString();

                        //Query the blockchain again for this specific transaction details
                        blockdata = "";
                        lock (dex.blockhandle)
                        { //Prevents other threads from doing blocking calls to connection
                            dex.blockhandle.Reset();
                            SendElectrumAction(dex, 4, txid); //Get the transaction information
                            dex.blockhandle.WaitOne(10000); //This will wait 10 seconds for a response
                            if (dex.blockdata == "")
                            {
                                NebliDexNetLog("No electrum response");
                                //Disconnect from server so it doesn't happen again
                                dex.open = false;
                                return "";
                            } //No response
                            blockdata = dex.blockdata;
                        }

                        result = JObject.Parse(blockdata);
                        if (result["result"] == null)
                        {
                            NebliDexNetLog("Unable to read transaction information: " + txid);
                            dex.open = false; //If an electrum server can't read a transaction, switch servers
                            return "";
                        }
                        result = (JObject)result["result"]; //Convert to the result

                        JArray result_vins = (JArray)result["vin"]; //Get all the Vins
                        for (int i = 0; i < result_vins.Count; i++)
                        {
                            string asm_string = result_vins[i]["scriptSig"]["asm"].ToString();
                            string secret = ExtractAtomicSwapSecretFromASM(asm_string, secrethash);
                            if (secret.Length > 0)
                            {
                                return secret;
                            }
                        }
                    }

                    NebliDexNetLog("Unable to extract secret from transactions linked to address: " + address);
                    return "";
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to parse electrum data: " + e.ToString());
                }
                return "";
            }
            else
            {
                try
                {
                    string response = "";
                    JObject result = null;
                    if (using_blockhelper == false)
                    {
                        if (using_cnblockhelper == false)
                        {
                            string nodeurl = NEBLAPI_server; //Currently only one node at this time
                            bool timeout;
                            response = HttpRequest(nodeurl + "/ins/addr/" + address, "", out timeout);
                            if (response.Length == 0) { return ""; }
                            result = JObject.Parse(response);
                            if (result["code"] != null) { return ""; } //This is also an error

                            JArray tx_array = (JArray)result["transactions"];
                            if (tx_array.Count < 2)
                            {
                                //Only one transaction, should be input
                                NebliDexNetLog("No spending transaction yet at address: " + address);
                                return "";
                            }

                            //Go through each transaction from last to first, looking for the secret
                            for (int txit = tx_array.Count - 1; txit >= 0; txit--)
                            {
                                string txid = tx_array[txit].ToString();

                                //Now get the transaction information
                                response = HttpRequest(nodeurl + "/ins/tx/" + txid, "", out timeout);
                                if (response.Length == 0) { return ""; }
                                result = JObject.Parse(response);
                                if (result["code"] != null)
                                {
                                    NebliDexNetLog("Unable to read transaction information: " + txid);
                                    return "";
                                } //This is also an error           

                                JArray result_vins = (JArray)result["vin"]; //Get all the Vins
                                for (int i = 0; i < result_vins.Count; i++)
                                {
                                    string asm_string = result_vins[i]["scriptSig"]["asm"].ToString();
                                    string secret = ExtractAtomicSwapSecretFromASM(asm_string, secrethash);
                                    if (secret.Length > 0)
                                    {
                                        return secret;
                                    }
                                }
                            }

                            NebliDexNetLog("Unable to extract secret from transactions linked to address: " + address);
                            return "";

                        }
                        else
                        {
                            //Querying the CN for spent UTXO information
                            JObject req = new JObject();
                            req["cn.method"] = "blockhelper.getspentaddressinfo";
                            req["cn.response"] = 0;
                            req["cn.address"] = address;
                            req["cn.max_utxo"] = 1; //Will only want the most recently spent UTXO
                            response = SendBlockHelperMessage(req, false);
                            if (response.Length == 0)
                            {
                                return "";
                            }
                            result = JObject.Parse(response);
                            if (result["cn.result"] == null)
                            {
                                //This means error
                                NebliDexNetLog("Unable to acquire spent UTXO for neblio address " + address + ", error: " + result["cn.error"].ToString());
                                return "";
                            }
                            result["stxos"] = result["cn.result"];
                            result.Remove("cn.result");
                        }
                    }
                    else
                    {
                        //Querying our own blockhelper for information
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.getspentaddressinfo";
                        req["rpc.response"] = 0;
                        req["rpc.neb_address"] = address;
                        req["rpc.max_utxo"] = 1;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0)
                        {
                            return "";
                        }
                        result = JObject.Parse(response);
                        if (result["rpc.result"] == null)
                        {
                            //This means error
                            NebliDexNetLog("Unable to acquire balance for neblio address " + address + ", error: " + result["rpc.error"].ToString());
                            return "";
                        }
                        result["stxos"] = result["rpc.result"];
                        result.Remove("rpc.result");
                    }

                    //Now for the blockhelper methods, just look at the most recently spent utxo
                    //BlockHelper returns the most recently spent UTXO
                    JArray utxo_array = (JArray)result["stxos"];
                    if (utxo_array.Count == 0)
                    {
                        NebliDexNetLog("No spending UTXO yet at address: " + address);
                        return "";
                    }

                    for (int i = 0; i < utxo_array.Count; i++)
                    {
                        string asm_string = utxo_array[i]["spendsig"].ToString();
                        string secret = ExtractAtomicSwapSecretFromASM(asm_string, secrethash);
                        if (secret.Length > 0)
                        {
                            return secret;
                        }
                    }

                    NebliDexNetLog("Unable to extract secret from address " + address);
                    return "";
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to extract secret data: " + e.ToString());
                }
                return "";
            }
        }

        public static string CreateAtomicSwapSecret()
        {
            //This will return a hex string of a cryptographically random byte array
            byte[] rand = RandomUtils.GetBytes(33); //Fixed length of 33 bytes
            return ConvertByteArrayToHexString(rand);
        }

        public static Script CreateAtomicSwapScript(string destination_add, string refund_add, string secret_hash_hex, long unlock_time)
        {
            //Secret_hash is a hex string representing a byte array
            byte[] des_pkh = ConvertHexStringToByteArray(Address2PublicKeyHash(destination_add));
            byte[] ref_pkh = ConvertHexStringToByteArray(Address2PublicKeyHash(refund_add));
            //Our secret must be exactly 33 bytes long
            //This is the smart contract
            Script contract = new Script(
                "OP_IF "
                + "OP_SIZE " + Op.GetPushOp(33) + " OP_EQUALVERIFY "
                + "OP_SHA256 " + Op.GetPushOp(ConvertHexStringToByteArray(secret_hash_hex)) + " OP_EQUALVERIFY "
                + "OP_DUP OP_HASH160 " + Op.GetPushOp(des_pkh) +
                " OP_ELSE "
                + Op.GetPushOp(unlock_time) + " OP_CHECKLOCKTIMEVERIFY OP_DROP "
                + "OP_DUP OP_HASH160 " + Op.GetPushOp(ref_pkh) +
                " OP_ENDIF "
                + "OP_EQUALVERIFY OP_CHECKSIG");
            return contract;
        }

        public static string CreateAtomicSwapAddress(int cointype, Script contract)
        {
            //This is the script, now convert it to a scripthash
            string scripthash_add = "";
            lock (transactionLock)
            {
                Network my_net;
                if (testnet_mode == false)
                {
                    my_net = Network.Main;
                }
                else
                {
                    my_net = Network.TestNet;
                }
                ChangeVersionByte(cointype, ref my_net);
                scripthash_add = contract.Hash.GetAddress(my_net).ToString();
            }
            return scripthash_add;
        }

    }
    
}