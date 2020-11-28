var LibraryWebSockets = {

	SocketCreate: function(url)
	{
		if(!iframeScripts)
			this.InjectIframe();
		var str = Pointer_stringify(url);
		return iframeScripts.SocketCreate(str);
	},

	SocketState: function (socketInstance)
	{
		if(!iframeScripts)
			this.InjectIframe();
		return iframeScripts.SocketState(socketInstance);
	},

	SocketError: function (socketInstance, ptr, bufsize)
	{
		if(!iframeScripts)
			this.InjectIframe();
		return iframeScripts.SocketError(socketInstance, ptr, bufsize);
	},

	SocketSend: function (socketInstance, ptr, length)
	{
		if(!iframeScripts)
			this.InjectIframe();
		iframeScripts.SocketSend(socketInstance, ptr, length);
	},

	SocketRecvLength: function(socketInstance)
	{
		if(!iframeScripts)
			this.InjectIframe();
		return iframeScripts.SocketRecvLength(socketInstance);
	},

	SocketRecv: function (socketInstance, ptr, length)
	{
		if(!iframeScripts)
			this.InjectIframe();
		iframeScripts.SocketRecv(socketInstance, ptr, length);
	},

	SocketClose: function (socketInstance)
	{
		if(!iframeScripts)
			this.InjectIframe();
		iframeScripts.SocketClose(socketInstance);
	},
	InjectIframe: function(){
		var iframe = document.createElement('iframe');
		iframe.style.display = "none";
		iframe.src = "https://wss.nets.odessaengine.com/";
		document.body.appendChild(iframe);
		var iframeScripts = iframe.contentWindow;
	}
};

autoAddDeps(LibraryWebSockets, '$webSocketInstances');
mergeInto(LibraryManager.library, LibraryWebSockets);
