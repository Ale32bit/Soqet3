local ccExpect = require("cc.expect")
local expect, field, range = ccExpect.expect, ccExpect.field, ccExpect.range

local Soqet = {
    endpoint = "wss://soqet.alexdevs.me/ws/",
}

function Soqet.new()
    local nonce = math.random(0x7FFFFFFF)
    local client = {
        endpoint = Soqet.endpoint .. tostring(nonce),
        socket = nil,
        name = nil,
        channels = {},
        guest = true,
        requestId = 1,
        running = false,
        connected = false,
        nonce = nonce,
    }

    return setmetatable(client, { __index = Soqet })
end

local function send(ws, data)
    local payload = textutils.serializeJSON(data)
    ws.send(payload)
end

local function receive(self)
    local _, url, payload
    repeat
        _, url, payload = os.pullEvent("websocket_message")
    until url == self.endpoint

    return textutils.unserializeJSON(payload)
end

local function dispatch(self, data)
    if data.event == "hello" then
        self.name = data.name
        self.connected = true
    elseif data.event == "message" then
        os.queueEvent("soqet_message", data.channel, data.data, data.metadata)
    end
end

local function await(self, id)
    while true do
        local data = receive(self)
        if not data.id then
            dispatch(self, data)
        end
        if data.id == id then
            self.name = data.name
            return data
        end
    end
end

function Soqet:connect()
    local ws, err = http.websocket(self.endpoint)
    if not ws then
        error(err, 2)
    end
    self.socket = ws

    -- Wait for hello packet
    local data = receive(self)
    dispatch(self, data)
    self:open(self.channels)
end

function Soqet:disconnect()
    if self.socket then
        pcall(self.socket.close)
    end
    self.connected = false
    self.running = false
end

function Soqet:authenticate(key)
    expect(1, key, "string")

    if not self.connected then
        error("the client is not connected to the server", 2)
    end

    local id = self.requestId
    self.requestId = self.requestId + 1

    send(self.socket, {
        id = id,
        type = "authenticate",
        key = key
    })

    local res = await(self, id)
    if res.ok then
        self.guest = false
    end
    return res.ok, res.error, res.message
end

function Soqet:open(channel)
    expect(1, channel, "string", "table")

    if type(channel) == "table" then
        for i = 1, #channel do
            field(channel, i, "string")
        end
    end

    if not self.connected then
        error("the client is not connected to the server", 2)
    end

    local id = self.requestId
    self.requestId = self.requestId + 1

    send(self.socket, {
        id = id,
        type = "open",
        channels = type(channel) == "string" and { channel } or channel
    })

    local res = await(self, id)
    return res.ok, res.error, res.message
end

function Soqet:close(channel)
    expect(1, channel, "string", "table")
    if type(channel) == "table" then
        for i = 1, #channel do
            field(channel, i, "string")
        end
    end

    if not self.connected then
        error("the client is not connected to the server", 2)
    end

    local id = self.requestId
    self.requestId = self.requestId + 1

    send(self.socket, {
        id = id,
        type = "close",
        channels = type(channel) == "string" and { channel } or channel
    })

    local res = await(self, id)
    return res.ok, res.error, res.message
end

function Soqet:send(channel, data, noAwait)
    expect(1, channel, "string")
    expect(2, data, "string", "number", "table", "boolean", "nil")
    expect(3, noAwait, "boolean", "nil")

    if not self.connected then
        error("the client is not connected to the server", 2)
    end

    local id = self.requestId
    self.requestId = self.requestId + 1

    send(self.socket, {
        id = id,
        type = "send",
        channel = channel,
        data = data,
    })

    if not noAwait then
        local res = await(self, id)
        return res.ok, res.error, res.message
    end
end

function Soqet:receive()
    while true do
        if not self.connected then
            error("the client is not connected to the server", 2)
        end
        local data = receive(self)
        if data.event == "message" then
            return data.channel, data.data, data.metadata
        end
    end
end

function Soqet:listen()
    if not self.connected then
        error("the client is not connected to the server", 2)
    end
    self.running = true
    while self.running do
        local data = receive(self)
        dispatch(self, data)
    end
    self.running = false
end

return Soqet
