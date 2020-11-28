var LibraryWebSockets = {

	SocketCreate: function(url)
	{
		var str = Pointer_stringify(url);
		return window.iframeScripts.SocketCreate(str);
	},

	SocketState: function (socketInstance)
	{
		return window.iframeScripts.SocketState(socketInstance);
	},

	SocketError: function (socketInstance, ptr, bufsize)
	{
		return window.iframeScripts.SocketError(socketInstance, ptr, bufsize);
	},

	SocketSend: function (socketInstance, ptr, length)
	{
		window.iframeScripts.SocketSend(socketInstance, ptr, length);
	},

	SocketRecvLength: function(socketInstance)
	{
		return window.iframeScripts.SocketRecvLength(socketInstance);
	},

	SocketRecv: function (socketInstance, ptr, length)
	{
		window.iframeScripts.SocketRecv(socketInstance, ptr, length);
	},

	SocketClose: function (socketInstance)
	{
		window.iframeScripts.SocketClose(socketInstance);
	},
	InjectIframe: function(){
		var iframe = document.createElement('iframe');
		iframe.style.display = "none";
		iframe.src = "https://wss.nets.odessaengine.com/";
		document.body.appendChild(iframe);
		window.iframeScripts = iframe.contentWindow;
	}
};

autoAddDeps(LibraryWebSockets, '$webSocketInstances');
mergeInto(LibraryManager.library, LibraryWebSockets);
