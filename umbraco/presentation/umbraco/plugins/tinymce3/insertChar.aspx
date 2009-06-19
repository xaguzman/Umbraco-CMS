<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="insertChar.aspx.cs" Inherits="umbraco.presentation.umbraco.plugins.tinymce3.insertChar" %>
<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Strict//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd">
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
	<title><%= umbraco.ui.Text("insertCharacter")%></title>
	<meta http-equiv="Content-Type" content="text/html; charset=ISO-8859-1" />
	<script type="text/javascript" src="/umbraco_client/tinymce3/tiny_mce_popup.js"></script>
	<script type="text/javascript" src="/umbraco_client/tinymce3/themes/umbraco/js/charmap.js"></script>
	<base target="_self" />
</head>
<body id="charmap" style="display:none">
<table align="center" border="0" cellspacing="0" cellpadding="2">
    <tr>
        <td id="charmapView" rowspan="2" align="left" valign="top">
			<!-- Chars will be rendered here -->
        </td>
        <td width="100" align="center" valign="top">
            <table border="0" cellpadding="0" cellspacing="0" width="100" style="height:100px">
                <tr>
                    <td id="codeV">&nbsp;</td>
                </tr>
                <tr>
                    <td id="codeN">&nbsp;</td>
                </tr>
            </table>
        </td>
    </tr>
    <tr>
        <td valign="bottom" style="padding-bottom: 3px;">
            <table width="100" align="center" border="0" cellpadding="2" cellspacing="0">
                <tr>
                    <td align="center" style="border-left: 1px solid #666699; border-top: 1px solid #666699; border-right: 1px solid #666699;">HTML-Code</td>
                </tr>
                <tr>
                    <td style="font-size: 16px; font-weight: bold; border-left: 1px solid #666699; border-bottom: 1px solid #666699; border-right: 1px solid #666699;" id="codeA" align="center">&nbsp;</td>
                </tr>
                <tr>
                    <td style="font-size: 1px;">&nbsp;</td>
                </tr>
                <tr>
                    <td align="center" style="border-left: 1px solid #666699; border-top: 1px solid #666699; border-right: 1px solid #666699;">NUM-Code</td>
                </tr>
                <tr>
                    <td style="font-size: 16px; font-weight: bold; border-left: 1px solid #666699; border-bottom: 1px solid #666699; border-right: 1px solid #666699;" id="codeB" align="center">&nbsp;</td>
                </tr>
            </table>
        </td>
    </tr>
</table>

</body>
</html>
