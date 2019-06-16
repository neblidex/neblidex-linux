using System;
namespace NebliDex_Linux
{
    public partial class DepositWindow : Gtk.Window
    {
        public DepositWindow() :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
            //Old height was 250
			Gtk.Label close_label = (Gtk.Label)Close_Button.Children[0];
            close_label.Markup = "<span font='14'>Close</span>";
			Close_Button.Clicked += Close_Dialog;

			string addre = "";
            for (int i = 0; i < App.WalletList.Count; i++)
            {
                Coin_Box.AppendText(App.WalletList[i].Coin);
                if (App.WalletList[i].type == 0)
                {
                    addre = App.WalletList[i].address;
                }
            }
            Coin_Box.Active = 0;
			Gtk.CellRendererText my_rend = (Gtk.CellRendererText)Coin_Box.Cells[0];
            my_rend.Scale = 1.4;
			Coin_Box.Changed += Change_Coin;

            Deposit_Address.Text = addre;
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
