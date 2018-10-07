<%@ Page Language="C#" CodeBehind="Default.aspx.cs" Inherits="SystemWebpackTestApp.Default" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
</head>
<body>
    <form id="form1" class="main-container" runat="server">
        <h1>
            SystemWebpack
            <small>Webpack and HMR support for System.Web projects</small>
        </h1>

        <div>
            I am ye olde web forms page.
        </div>
        
        <div>
            The button below is used through dynamically loaded scripts.
        </div>
        
        <p>
            <button id="hello-button" type="button">Say hello</button>
        </p>
        
        <script src="/build/main.js"></script>
    </form>
</body>
</html>
