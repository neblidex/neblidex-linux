using System;
namespace NebliDex_Linux
{
    public partial class DepositWindow : Gtk.Window
    {
        public DepositWindow() :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
			this.Hide();
            //Old height was 250
			Gtk.Label close_label = (Gtk.Label)Close_Button.Children[0];
            close_label.Markup = "<span font='14'>Close</span>";
			Close_Button.Clicked += Close_Dialog;
            
            for (int i = 0; i < App.WalletList.Count; i++)
            {
                Coin_Box.AppendText(App.WalletList[i].Coin);
            }
            Coin_Box.Active = 0;
			string addre = App.WalletList[0].address;
			if(App.WalletList[0].blockchaintype == 4){
				addre = SharpCashAddr.Converter.ToCashAddress(addre);
			}

			Gtk.CellRendererText my_rend = (Gtk.CellRendererText)Coin_Box.Cells[0];
            my_rend.Scale = 1.4;
			Coin_Box.Changed += Change_Coin;

            Deposit_Address.Text = addre;
			this.Show();
        }

		private void Close_Dialog(object sender, EventArgs e)
        {
			this.Destroy();
        }

		private void Change_Coin(object sender, EventArgs e)
        {
			//First find which one was selected
			string which_coin = (string)Coin_Box.ActiveText;

            string addre = "";
            for (int i = 0; i < App.WalletList.Count; i++)
            {
                if (App.WalletList[i].Coin == which_coin)
                {
                    addre = App.WalletList[i].address;
					if (App.WalletList[i].blockchaintype == 4)
                    { //If Bitcoin Cash
                        addre = SharpCashAddr.Converter.ToCashAddress(addre); //Show the cash address for deposits
                    }
                    break;
                }
            }

            if (Deposit_Address != null)
            {
                Deposit_Address.Text = addre;
            }
        }
    }
}
