/* UniversalTelegramBot.h -- o bot do Telegram (Brian Lough) pela API de
 * verdade: o sendMessage e o getUpdates sao os do simulador, e as mensagens
 * que chegaram sao lidas dele (bot.messages[i].text). */
#ifndef UniversalTelegramBot_h
#define UniversalTelegramBot_h

#include "cs_objeto.h"
#include "WiFiClient.h"

#define TELEGRAM_HOST "api.telegram.org"
#define TELEGRAM_SSL_PORT 443
#define HANDLE_MESSAGES 1

struct telegramMessage {
    String text;
    String chat_id;
    String chat_title;
    String from_id;
    String from_name;
    String date;
    String type;
    String file_caption;
    String file_path;
    String file_name;
    bool hasDocument = false;
    long file_size = 0;
    float longitude = 0;
    float latitude = 0;
    int update_id = 0;
    int message_id = 0;
    int reply_to_message_id = 0;
    String reply_to_text;
    String query_id;
};

class UniversalTelegramBot : public CsObjeto {
    /* "bot.messages[i]": a mensagem i da ultima leva do getUpdates. */
    struct Mensagens {
        UniversalTelegramBot *bot;
        telegramMessage operator[](int i) const
        {
            telegramMessage m;
            char caminho[48];
            auto ler = [&](const char *campo) {
                snprintf(caminho, sizeof(caminho), "messages.%d.%s", i, campo);
                return cs_obj_t(nullptr, "__ler", cs_nome_de{bot->cs_nome}, caminho);
            };
            m.text = ler("text");
            m.chat_id = ler("chat_id");
            m.from_name = ler("from_name");
            m.type = ler("type");
            m.update_id = ler("update_id").toInt();
            m.from_id = m.chat_id;
            return m;
        }
    };

public:
    Mensagens messages{this};
    long last_message_received = 0;
    String name;
    String userName;
    int longPoll = 0;
    unsigned int waitForResponse = 1500;
    int maxMessageLength = 1500;

    UniversalTelegramBot(const String &token, Client &client)
    {
        (void)client;
        cs_criar("UniversalTelegramBot", token);
    }
    UniversalTelegramBot(const UniversalTelegramBot &) = delete;

    bool sendMessage(const String &chat_id, const String &text, const String &parse_mode = "")
    {
        return cs_obj_i(cs_nome, "sendMessage", chat_id, text, parse_mode) != 0;
    }
    bool sendMessageWithReplyKeyboard(const String &chat_id, const String &text, const String &parse_mode,
                                      const String &keyboard, bool resize = false, bool oneTime = false, bool selective = false)
    {
        (void)keyboard;
        (void)resize;
        (void)oneTime;
        (void)selective;
        return sendMessage(chat_id, text, parse_mode);
    }
    bool sendMessageWithInlineKeyboard(const String &chat_id, const String &text, const String &parse_mode,
                                       const String &keyboard, int message_id = 0)
    {
        (void)keyboard;
        (void)message_id;
        return sendMessage(chat_id, text, parse_mode);
    }
    bool sendChatAction(const String &chat_id, const String &text) { (void)chat_id; (void)text; return true; }
    bool setMyCommands(const String &commandArray) { (void)commandArray; return true; }
    bool getMe() { return true; }
    void updateToken(const String &token) { (void)token; }
    int getUpdates(long offset)
    {
        int n = (int)cs_obj_i(cs_nome, "getUpdates", offset);
        last_message_received = (long)cs_obj_i(nullptr, "__ler", cs_nome_de{cs_nome}, "last_message_received");
        return n;
    }
    bool answerCallbackQuery(const String &query_id, const String &text = "", bool show_alert = false,
                             const String &url = "", int cache_time = 0)
    {
        (void)query_id;
        (void)text;
        (void)show_alert;
        (void)url;
        (void)cache_time;
        return true;
    }
};

#endif
