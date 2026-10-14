/* As listas encadeadas do BSD que o ESP-IDF usa (SLIST, STAILQ, LIST, TAILQ). */
#ifndef _SYS_QUEUE_H
#define _SYS_QUEUE_H

#define SLIST_HEAD(nome, tipo) struct nome { struct tipo *slh_first; }
#define SLIST_HEAD_INITIALIZER(cabeca) { NULL }
#define SLIST_ENTRY(tipo) struct { struct tipo *sle_next; }
#define SLIST_FIRST(cabeca) ((cabeca)->slh_first)
#define SLIST_EMPTY(cabeca) ((cabeca)->slh_first == NULL)
#define SLIST_NEXT(elm, campo) ((elm)->campo.sle_next)
#define SLIST_INIT(cabeca) do { (cabeca)->slh_first = NULL; } while (0)
#define SLIST_INSERT_HEAD(cabeca, elm, campo) do { (elm)->campo.sle_next = (cabeca)->slh_first; (cabeca)->slh_first = (elm); } while (0)
#define SLIST_INSERT_AFTER(slistelm, elm, campo) do { (elm)->campo.sle_next = (slistelm)->campo.sle_next; (slistelm)->campo.sle_next = (elm); } while (0)
#define SLIST_REMOVE_HEAD(cabeca, campo) do { (cabeca)->slh_first = (cabeca)->slh_first->campo.sle_next; } while (0)
#define SLIST_FOREACH(var, cabeca, campo) for ((var) = SLIST_FIRST(cabeca); (var); (var) = SLIST_NEXT(var, campo))
#define SLIST_FOREACH_SAFE(var, cabeca, campo, tvar) for ((var) = SLIST_FIRST(cabeca); (var) && ((tvar) = SLIST_NEXT(var, campo), 1); (var) = (tvar))

#define STAILQ_HEAD(nome, tipo) struct nome { struct tipo *stqh_first; struct tipo **stqh_last; }
#define STAILQ_HEAD_INITIALIZER(cabeca) { NULL, &(cabeca).stqh_first }
#define STAILQ_ENTRY(tipo) struct { struct tipo *stqe_next; }
#define STAILQ_FIRST(cabeca) ((cabeca)->stqh_first)
#define STAILQ_EMPTY(cabeca) ((cabeca)->stqh_first == NULL)
#define STAILQ_NEXT(elm, campo) ((elm)->campo.stqe_next)
#define STAILQ_INIT(cabeca) do { (cabeca)->stqh_first = NULL; (cabeca)->stqh_last = &(cabeca)->stqh_first; } while (0)
#define STAILQ_INSERT_TAIL(cabeca, elm, campo) do { (elm)->campo.stqe_next = NULL; *(cabeca)->stqh_last = (elm); (cabeca)->stqh_last = &(elm)->campo.stqe_next; } while (0)
#define STAILQ_INSERT_HEAD(cabeca, elm, campo) do { if (((elm)->campo.stqe_next = (cabeca)->stqh_first) == NULL) (cabeca)->stqh_last = &(elm)->campo.stqe_next; (cabeca)->stqh_first = (elm); } while (0)
#define STAILQ_REMOVE_HEAD(cabeca, campo) do { if (((cabeca)->stqh_first = (cabeca)->stqh_first->campo.stqe_next) == NULL) (cabeca)->stqh_last = &(cabeca)->stqh_first; } while (0)
#define STAILQ_FOREACH(var, cabeca, campo) for ((var) = STAILQ_FIRST(cabeca); (var); (var) = STAILQ_NEXT(var, campo))

#define LIST_HEAD(nome, tipo) struct nome { struct tipo *lh_first; }
#define LIST_HEAD_INITIALIZER(cabeca) { NULL }
#define LIST_ENTRY(tipo) struct { struct tipo *le_next; struct tipo **le_prev; }
#define LIST_FIRST(cabeca) ((cabeca)->lh_first)
#define LIST_EMPTY(cabeca) ((cabeca)->lh_first == NULL)
#define LIST_NEXT(elm, campo) ((elm)->campo.le_next)
#define LIST_INIT(cabeca) do { (cabeca)->lh_first = NULL; } while (0)
#define LIST_INSERT_HEAD(cabeca, elm, campo) do { if (((elm)->campo.le_next = (cabeca)->lh_first) != NULL) (cabeca)->lh_first->campo.le_prev = &(elm)->campo.le_next; (cabeca)->lh_first = (elm); (elm)->campo.le_prev = &(cabeca)->lh_first; } while (0)
#define LIST_REMOVE(elm, campo) do { if ((elm)->campo.le_next != NULL) (elm)->campo.le_next->campo.le_prev = (elm)->campo.le_prev; *(elm)->campo.le_prev = (elm)->campo.le_next; } while (0)
#define LIST_FOREACH(var, cabeca, campo) for ((var) = LIST_FIRST(cabeca); (var); (var) = LIST_NEXT(var, campo))

#define TAILQ_HEAD(nome, tipo) struct nome { struct tipo *tqh_first; struct tipo **tqh_last; }
#define TAILQ_HEAD_INITIALIZER(cabeca) { NULL, &(cabeca).tqh_first }
#define TAILQ_ENTRY(tipo) struct { struct tipo *tqe_next; struct tipo **tqe_prev; }
#define TAILQ_FIRST(cabeca) ((cabeca)->tqh_first)
#define TAILQ_EMPTY(cabeca) ((cabeca)->tqh_first == NULL)
#define TAILQ_NEXT(elm, campo) ((elm)->campo.tqe_next)
#define TAILQ_INIT(cabeca) do { (cabeca)->tqh_first = NULL; (cabeca)->tqh_last = &(cabeca)->tqh_first; } while (0)
#define TAILQ_INSERT_TAIL(cabeca, elm, campo) do { (elm)->campo.tqe_next = NULL; (elm)->campo.tqe_prev = (cabeca)->tqh_last; *(cabeca)->tqh_last = (elm); (cabeca)->tqh_last = &(elm)->campo.tqe_next; } while (0)
#define TAILQ_INSERT_HEAD(cabeca, elm, campo) do { if (((elm)->campo.tqe_next = (cabeca)->tqh_first) != NULL) (cabeca)->tqh_first->campo.tqe_prev = &(elm)->campo.tqe_next; else (cabeca)->tqh_last = &(elm)->campo.tqe_next; (cabeca)->tqh_first = (elm); (elm)->campo.tqe_prev = &(cabeca)->tqh_first; } while (0)
#define TAILQ_REMOVE(cabeca, elm, campo) do { if (((elm)->campo.tqe_next) != NULL) (elm)->campo.tqe_next->campo.tqe_prev = (elm)->campo.tqe_prev; else (cabeca)->tqh_last = (elm)->campo.tqe_prev; *(elm)->campo.tqe_prev = (elm)->campo.tqe_next; } while (0)
#define TAILQ_FOREACH(var, cabeca, campo) for ((var) = TAILQ_FIRST(cabeca); (var); (var) = TAILQ_NEXT(var, campo))

#endif
