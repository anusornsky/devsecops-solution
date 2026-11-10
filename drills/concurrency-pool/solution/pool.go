// เฉลย: worker pool คงที่ limit ตัว + ช่องส่งงาน (channel) · ยกเลิกแล้วหยุดป้อนงาน
// ทางที่ไม่เลือก: semaphore (channel ขนาด limit) + goroutine ต่องาน — ก็ถูก แต่ถ้ามี 1 ล้านงานจะสร้าง goroutine 1 ล้านตัวรอคิว
package pool

import (
	"context"
	"sync"
)

func Run(ctx context.Context, jobs []int, limit int, work func(context.Context, int) error) error {
	ctx, cancel := context.WithCancel(ctx)
	defer cancel() // ปลด goroutine ภายในของ WithCancel เสมอ (ไม่งั้นรั่ว)

	feed := make(chan int)
	var wg sync.WaitGroup
	var once sync.Once
	var first error
	fail := func(err error) { once.Do(func() { first = err; cancel() }) } // error แรก = หยุดทั้งชุด

	for i := 0; i < limit; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for j := range feed { // feed ปิด = ไม่มีงานแล้ว worker ออกเอง
				if err := work(ctx, j); err != nil {
					fail(err)
				}
			}
		}()
	}

	parent := ctx
send:
	for _, j := range jobs {
		select {
		case feed <- j:
		case <-parent.Done(): // ยกเลิก → เลิกป้อน · งานที่กำลังทำปล่อยให้จบ
			break send
		}
	}
	close(feed)
	wg.Wait()

	if first != nil {
		return first
	}
	return parent.Err() // nil ถ้าทำครบ · context.Canceled ถ้าถูกยกเลิกจากข้างนอก
}
