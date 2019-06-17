using System;
using Gtk;

namespace NebliDex_Linux
{
	public partial class App
	{
		public static bool PromptUser(Window parent, string title, string message,string okstring,string nostring)
        {
			
			Dialog dialog = null;
            ResponseType response = ResponseType.None;

            try
            {
                dialog = new Dialog(
                    title,
                    parent,
                    DialogFlags.DestroyWithParent | DialogFlags.Modal,
                    okstring, ResponseType.Yes,
                   nostring, ResponseType.No
                );
				//dialog.DefaultHeight = 100;
				dialog.SetPosition(WindowPosition.CenterOnParent);
				//dialog.HeightRequest = 100;
				dialog.BorderWidth = 10;
				dialog.Resizable = false;
                dialog.VBox.Add(new Label(""+message+"\n"));
                dialog.ShowAll();

                response = (ResponseType)dialog.Run();
            }
            finally
            {
				if (dialog != null){
					dialog.Destroy();
				}  
            }

			if (response == ResponseType.Yes){
				return true;
			}else{
				return false;
			}
        }

		public static void MessageBox(Window parent, string title, string message, string okstring)
        {

            Dialog dialog = null;
            ResponseType response = ResponseType.None;

            try
            {
                dialog = new Dialog(
                    title,
                    parent,
                    DialogFlags.DestroyWithParent | DialogFlags.Modal,
					okstring, ResponseType.Yes
				);

				dialog.SetPosition(WindowPosition.CenterOnParent);
                //dialog.HeightRequest = 100;
				dialog.BorderWidth = 10;
                dialog.Resizable = false;
                dialog.VBox.Add(new Label("" + message + "\n"));
                dialog.ShowAll();
				dialog.KeepAbove = true;

                response = (ResponseType)dialog.Run();
            }
            finally
            {
				if (dialog != null){
                    dialog.Destroy();
                }
            }

        }
	}
}
